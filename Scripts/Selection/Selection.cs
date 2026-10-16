using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// What one player has selected in one place, which every player sees.
/// Each player writes only their own, so selections never overwrite each other.
/// Selections are not saved with the project.
/// </summary>
[NotSaved]
[JsonName("Selection")]
public record Selection : Replicated
{
    public static readonly Id<Selection, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// Every player's selection in each place, by <see cref="Within"/>.
    /// </summary>
    public static readonly Group<Selection, SnowTag> ByWithin = new(s => s.Within);

    /// <summary>
    /// Each player's selections, by <see cref="Player"/>.
    /// </summary>
    public static readonly Group<Selection, byte> ByPlayer = new(s => s.Player);

    /// <summary>The source of the player who owns this selection.</summary>
    public byte Player { get; init; }

    /// <summary>
    /// Where it was made: empty for the table, or the dataset whose rows and columns it holds.
    /// A player has one selection in each place, so each view undoes only its own.
    /// </summary>
    public SnowTag Within { get; init; }

    /// <summary>The selected things.</summary>
    public ImmutableHashSet<Target> Targets { get; init; } = ImmutableHashSet<Target>.Empty;
}

/// <summary>
/// Reads and writes the local player's <see cref="Selection"/>, and computes selection highlights.
/// </summary>
public static class LocalSelection
{
    /// <summary>
    /// The selection color to show each target in.
    /// If <paramref name="within"/> is a SnowTag, gets the selection in that dataset
    /// If <paramref name="within"/> is <see cref="SnowTag.Empty"/>, gets the selection for the table.
    ///
    /// <para>
    /// A target shows in the seat color of the player who selected it most recently,
    /// with the local player taking priority.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<Target, Color> SelectionColors(
        this IRecordReader R,
        SnowTag within
    )
    {
        var local = Snowport.Clock.source;
        var colors = new Dictionary<Target, Color>();
        var newestFirst = R.Get(Selection.ByWithin, within)
            .OrderByDescending(s => s.Player == local)
            .ThenByDescending(R.Version);
        foreach (var selection in newestFirst)
        {
            var color = R.SeatColor(selection.Player);
            foreach (var target in selection.Targets)
                colors.TryAdd(target, color);
        }
        return colors;
    }

    /// <summary>
    /// The local player's selection <paramref name="within"/> a dataset, or on the table by default.
    /// Null if they have not yet selected anything there.
    /// </summary>
    public static Selection GetSelection(this IRecordReader R, SnowTag within = default) =>
        R.Get(Selection.ByWithin, within).FirstOrDefault(s => s.Player == Snowport.Clock.source);

    /// <summary>The ids of the records of type <typeparamref name="T"/> the local player has selected on the table.</summary>
    public static IEnumerable<SnowTag> GetSelection<T>(this IRecordReader R)
        where T : Replicated =>
        R.GetSelection()?.Targets.OfType<RecordTarget>().Select(t => t.Id).Where(id => R.Is<T>(id))
        ?? [];

    /// <summary>
    /// Replaces the local player's selected records of type <typeparamref name="T"/> on the table,
    /// keeping everything else they have selected there.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection<T>(this IRecordReader R, IEnumerable<SnowTag> ids)
        where T : Replicated => RecordService.Instance.Write(R.NewSelection<T>(ids));

    /// <summary>
    /// The record that <see cref="SetSelection{T}(IRecordReader, IEnumerable{SnowTag})"/> writes,
    /// or none when the selection wouldn't change.
    /// </summary>
    public static Selection[] NewSelection<T>(this IRecordReader R, IEnumerable<SnowTag> ids)
        where T : Replicated
    {
        var old = R.GetSelection()?.Targets ?? ImmutableHashSet<Target>.Empty;
        return R.NewSelection(
            old.Where(t => t is not RecordTarget r || !R.Is<T>(r.Id))
                .Concat(ids.Select(id => new RecordTarget(id)))
        );
    }

    /// <summary>
    /// Replaces the local player's whole selection <paramref name="within"/> a dataset, or on the table by default.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection(
        this IRecordReader R,
        IEnumerable<Target> targets,
        SnowTag within = default
    )
    {
        RecordService.Instance.Write(R.NewSelection(targets, within));
    }

    /// <summary>
    /// The record that will set the selection to <paramref name="targets"/>,
    /// or none when the selection wouldn't change.
    /// </summary>
    private static Selection[] NewSelection(
        this IRecordReader R,
        IEnumerable<Target> targets,
        SnowTag within = default
    )
    {
        var current = R.GetSelection(within);
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<Target>.Empty))
            return [];

        var record =
            current
            ?? new Selection
            {
                Id = Snowport.Clock.CreateTag(),
                Player = Snowport.Clock.source,
                Within = within,
            };
        return [record with { Targets = next }];
    }
}

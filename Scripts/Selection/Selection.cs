using System;
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
    /// <summary>The color the local player's own selection shows in, everywhere.</summary>
    public static readonly Color LocalColor = Colors.White;

    /// <summary>
    /// <para>The color to show each selected target in, among the selections <paramref name="which"/> picks.</para>
    ///
    /// <para>
    /// The local player's selection shows in <see cref="LocalColor"/>, over anyone else's.
    /// Otherwise a target shows in the seat color of the player who selected it most recently.
    /// The <paramref name="which"/> should filter as many selections as possible to avoid observing extraneous records.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<Target, Color> SelectionColors(
        this IRecordReader R,
        Func<Selection, bool> which
    )
    {
        var local = Snowport.Clock.source;
        var colors = new Dictionary<Target, Color>();
        var newestFirst = R.Get(which)
            .OrderByDescending(s => s.Player == local)
            .ThenByDescending(s => s.LastUpdateId);
        foreach (var selection in newestFirst)
        {
            var color = selection.Player == local ? LocalColor : R.SeatColor(selection.Player);
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
        R.Get<Selection>(s => s.Player == Snowport.Clock.source && s.Within == within)
            .FirstOrDefault();

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
        where T : Replicated
    {
        if (R.NewSelection<T>(ids) is { } selection)
            RecordService.Instance.Write(selection);
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
        if (R.NewSelection(targets, within) is { } selection)
            RecordService.Instance.Write(selection);
    }

    /// <summary>
    /// The record that will set the selection to <paramref name="ids"/>,
    /// or null when the selection wouldn't change.
    /// </summary>
    public static Selection NewSelection<T>(this IRecordReader R, IEnumerable<SnowTag> ids)
        where T : Replicated
    {
        var old = R.GetSelection()?.Targets ?? ImmutableHashSet<Target>.Empty;
        return R.NewSelection(
            old.Where(t => t is not RecordTarget r || !R.Is<T>(r.Id))
                .Concat(ids.Select(id => new RecordTarget(id)))
        );
    }

    /// <summary>
    /// The record that will set the selection to <paramref name="targets"/>,
    /// or null when the selection wouldn't change.
    /// </summary>
    public static Selection NewSelection(
        this IRecordReader R,
        IEnumerable<Target> targets,
        SnowTag within = default
    )
    {
        var current = R.GetSelection(within);
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<Target>.Empty))
            return null;

        var record =
            current
            ?? new Selection
            {
                Id = Snowport.Clock.CreateTag(),
                Player = Snowport.Clock.source,
                Within = within,
            };
        return record with { Targets = next };
    }
}

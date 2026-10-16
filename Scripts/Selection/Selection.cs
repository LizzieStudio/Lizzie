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
    public ImmutableHashSet<AtomRef> Targets { get; init; } = ImmutableHashSet<AtomRef>.Empty;
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
    public static IReadOnlyDictionary<AtomRef, Color> SelectionColors(
        this IRecordReader R,
        SnowTag within
    )
    {
        var local = Snowport.Clock.source;
        var colors = new Dictionary<AtomRef, Color>();
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

    /// <summary>
    /// The ids of the atoms of <paramref name="byId"/>'s type the local player has selected on the table.
    /// </summary>
    /// <param name="byId">Names the atom type and its id type, which the call infers from it.</param>
    public static IEnumerable<I> GetSelection<T, I>(this IRecordReader R, Id<T, I> byId)
        where T : Replicated
        where I : IComparable<I> =>
        R.GetSelection()?.Targets.Where(t => t.Type == typeof(T)).Select(t => (I)t.Id) ?? [];

    /// <summary>
    /// Replaces the local player's selected atoms of <paramref name="byId"/>'s type on the table,
    /// keeping everything else they have selected there.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection<T, I>(this IRecordReader R, Id<T, I> byId, IEnumerable<I> ids)
        where T : Replicated
        where I : IComparable<I> => RecordService.Instance.Write(R.NewSelection(byId, ids));

    /// <summary>
    /// The record that <see cref="SetSelection{T, I}(IRecordReader, Id{T, I}, IEnumerable{I})"/> writes,
    /// or none when the selection wouldn't change.
    /// </summary>
    public static Selection[] NewSelection<T, I>(
        this IRecordReader R,
        Id<T, I> byId,
        IEnumerable<I> ids
    )
        where T : Replicated
        where I : IComparable<I>
    {
        var old = R.GetSelection()?.Targets ?? ImmutableHashSet<AtomRef>.Empty;
        return R.NewSelection(old.Where(t => t.Type != typeof(T)).Concat(ids.Select(byId.Ref)));
    }

    /// <summary>
    /// Replaces the local player's whole selection <paramref name="within"/> a dataset, or on the table by default.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection(
        this IRecordReader R,
        IEnumerable<AtomRef> targets,
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
        IEnumerable<AtomRef> targets,
        SnowTag within = default
    )
    {
        var current = R.GetSelection(within);
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<AtomRef>.Empty))
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

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
public record Selection : Atom
{
    public static readonly Id<Selection, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// Every player's selection in each place, by <see cref="Place"/>.
    /// </summary>
    public static readonly Group<Selection, SelectionPlace> ByPlace = new(s => s.Place);

    /// <summary>
    /// Each player's selections, by <see cref="Player"/>.
    /// </summary>
    public static readonly Group<Selection, byte> ByPlayer = new(s => s.Player);

    /// <summary>The source of the player who owns this selection.</summary>
    public byte Player { get; init; }

    /// <summary>
    /// Where this selection lives.
    ///
    /// <para>
    /// A player has one selection in each place, so each view undoes only its own.
    /// </para>
    /// </summary>
    public SelectionPlace Place { get; init; }

    /// <summary>The selected things.</summary>
    public ImmutableHashSet<AtomRef> Targets { get; init; } = ImmutableHashSet<AtomRef>.Empty;
}

/// <summary>
/// Where a <see cref="Selection"/> lives.
///
/// <para>
/// Each view keeps its own selection.
/// Otherwise Undo/Redo would change your selection in closed windows.
/// </para>
/// </summary>
/// <param name="View">The view where the selection is.</param>
/// <param name="Within">The atom the view is showing, or empty when it shows a whole list.</param>
public readonly record struct SelectionPlace(SelectionPlaces View, SnowTag Within);

public enum SelectionPlaces
{
    Table = 1,
    DataSet = 2,
    Blueprints = 3,
}

/// <summary>
/// Reads and writes the local player's <see cref="Selection"/>, and computes selection highlights.
/// </summary>
public static class LocalSelection
{
    /// <summary>
    /// The selection color to show each target in.
    /// <paramref name="place"/> is where the selections live, like the stage the table shows.
    ///
    /// <para>
    /// A target shows in the seat color of the player who selected it most recently,
    /// with the local player taking priority.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<AtomRef, Color> SelectionColors(
        this IAtomReader R,
        SelectionPlace place
    )
    {
        var local = Snowport.Clock.source;
        var colors = new Dictionary<AtomRef, Color>();
        var newestFirst = R.Get(Selection.ByPlace, place)
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
    /// The local player's selection in <paramref name="place"/>.
    /// Null if they have not yet selected anything there.
    /// </summary>
    public static Selection GetSelection(this IAtomReader R, SelectionPlace place) =>
        R.Get(Selection.ByPlace, place).FirstOrDefault(s => s.Player == Snowport.Clock.source);

    /// <summary>
    /// The ids of the atoms of <paramref name="byId"/>'s type the local player has selected in <paramref name="place"/>.
    /// </summary>
    /// <param name="byId">Names the atom type and its id type, which the call infers from it.</param>
    public static IEnumerable<I> GetSelection<T, I>(
        this IAtomReader R,
        Id<T, I> byId,
        SelectionPlace place
    )
        where T : Atom
        where I : IComparable<I> =>
        R.GetSelection(place)?.Targets.Where(t => t.Type == typeof(T)).Select(t => (I)t.Id) ?? [];

    /// <summary>
    /// Replaces the local player's selected atoms of <paramref name="byId"/>'s type in <paramref name="place"/>,
    /// keeping everything else they have selected there.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection<T, I>(
        this IAtomReader R,
        Id<T, I> byId,
        IEnumerable<I> ids,
        SelectionPlace place
    )
        where T : Atom
        where I : IComparable<I> => AtomService.Instance.Write(R.NewSelection(byId, ids, place));

    /// <summary>
    /// The atom that <see cref="SetSelection{T, I}(IAtomReader, Id{T, I}, IEnumerable{I}, SelectionPlace)"/> writes,
    /// or none when the selection wouldn't change.
    /// </summary>
    public static Selection[] NewSelection<T, I>(
        this IAtomReader R,
        Id<T, I> byId,
        IEnumerable<I> ids,
        SelectionPlace place
    )
        where T : Atom
        where I : IComparable<I>
    {
        var old = R.GetSelection(place)?.Targets ?? ImmutableHashSet<AtomRef>.Empty;
        return R.NewSelection(
            old.Where(t => t.Type != typeof(T)).Concat(ids.Select(byId.Ref)),
            place
        );
    }

    /// <summary>
    /// Replaces the local player's whole selection in <paramref name="place"/>.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection(
        this IAtomReader R,
        IEnumerable<AtomRef> targets,
        SelectionPlace place
    )
    {
        AtomService.Instance.Write(R.NewSelection(targets, place));
    }

    /// <summary>
    /// The atom that will set the selection to <paramref name="targets"/>,
    /// or none when the selection wouldn't change.
    /// </summary>
    private static Selection[] NewSelection(
        this IAtomReader R,
        IEnumerable<AtomRef> targets,
        SelectionPlace place
    )
    {
        var current = R.GetSelection(place);
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<AtomRef>.Empty))
            return [];

        var atom =
            current
            ?? new Selection
            {
                Id = Snowport.Clock.CreateTag(),
                Player = Snowport.Clock.source,
                Place = place,
            };
        return [atom with { Targets = next }];
    }
}

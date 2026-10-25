using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// What one player has selected in one place, which every player sees.
/// Each player writes only their own, so selections never overwrite each other.
/// Selections are not saved with the project.
/// </summary>
[NotSaved]
[JsonName("Selection")]
public record Selection : Atom
{
    public static readonly Identity<Selection, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// Every player's selection in each place, by <see cref="Place"/>.
    /// </summary>
    public static readonly Lookup<Selection, ViewPlace> ByPlace = new(s => s.Place);

    /// <summary>
    /// Each player's selections, by <see cref="Player"/>.
    /// </summary>
    public static readonly Lookup<Selection, byte> ByPlayer = new(s => s.Player);

    /// <summary>The source of the player who owns this selection.</summary>
    public byte Player { get; init; }

    /// <summary>
    /// Where this selection lives.
    ///
    /// <para>
    /// A player has one selection in each place, so each view undoes only its own.
    /// </para>
    /// </summary>
    public ViewPlace Place { get; init; }

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
    /// <paramref name="place"/> is where the selections live, like the stage the view shows.
    ///
    /// <para>
    /// A target shows in the seat color of the player who selected it most recently,
    /// with the local player taking priority.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<AtomRef, Color> SelectionColors(
        this IAtomReader R,
        ViewPlace place
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
    /// The local player's selection in <paramref name="view"/>, holding only the targets it <see cref="ICommandView.Shows"/>.
    /// Null if the view has no selection, or they have not yet selected anything there.
    /// </summary>
    /// <remarks>
    /// A target the view no longer shows stays in the written selection, so undoing its delete selects it again,
    /// but it's left out here, so changing the selection around it never writes an event that shows nothing.
    /// </remarks>
    public static Selection GetSelection(this IAtomReader R, ICommandView view) =>
        view.Place(R) is { } place
        && R.Get(Selection.ByPlace, place).FirstOrDefault(s => s.Player == Snowport.Clock.source)
            is { } selection
            ? selection with
            {
                Targets = selection.Targets.Where(t => view.Shows(R, t)).ToImmutableHashSet(),
            }
            : null;

    /// <summary>
    /// The ids of the atoms of <paramref name="byId"/>'s type the local player has selected in <paramref name="view"/>.
    /// </summary>
    /// <param name="byId">Names the atom type and its id type, which the call infers from it.</param>
    public static IEnumerable<I> GetSelection<T, I>(
        this IAtomReader R,
        Identity<T, I> byId,
        ICommandView view
    )
        where T : Atom =>
        R.GetSelection(view)?.Targets.Where(t => t.Type == typeof(T)).Select(t => (I)t.Id) ?? [];

    /// <summary>
    /// Replaces the local player's selected atoms of <paramref name="byId"/>'s type in <paramref name="view"/>,
    /// keeping everything else they have selected there.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection<T, I>(
        this IAtomReader R,
        Identity<T, I> byId,
        IEnumerable<I> ids,
        ICommandView view
    )
        where T : Atom => AtomService.Instance.Write((Node)view, R.NewSelection(byId, ids, view));

    /// <summary>
    /// The atom that <see cref="SetSelection{T, I}(IAtomReader, Identity{T, I}, IEnumerable{I}, ICommandView)"/> writes,
    /// or none when the selection wouldn't change.
    /// </summary>
    public static Selection[] NewSelection<T, I>(
        this IAtomReader R,
        Identity<T, I> byId,
        IEnumerable<I> ids,
        ICommandView view
    )
        where T : Atom
    {
        var old = R.GetSelection(view)?.Targets ?? ImmutableHashSet<AtomRef>.Empty;
        return R.NewSelection(
            old.Where(t => t.Type != typeof(T)).Concat(ids.Select(byId.Ref)),
            view
        );
    }

    /// <summary>
    /// Replaces the local player's whole selection in <paramref name="view"/>.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection(
        this IAtomReader R,
        IEnumerable<AtomRef> targets,
        ICommandView view
    )
    {
        AtomService.Instance.Write((Node)view, R.NewSelection(targets, view));
    }

    /// <summary>
    /// The atom that will set the selection to <paramref name="targets"/>,
    /// or none when the selection wouldn't change or the view has no selection.
    /// </summary>
    private static Selection[] NewSelection(
        this IAtomReader R,
        IEnumerable<AtomRef> targets,
        ICommandView view
    )
    {
        if (view.Place(R) is not { } place)
            return [];
        var current = R.GetSelection(view);
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<AtomRef>.Empty))
            return [];

        var atom =
            current
            ?? new Selection
            {
                Id = SnowTag.Create(),
                Player = Snowport.Clock.source,
                Place = place,
            };
        return [atom with { Targets = next }];
    }
}

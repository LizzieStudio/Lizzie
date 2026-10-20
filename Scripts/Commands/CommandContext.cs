using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// What commands act on. Usually the selection or a hovered element.
/// </summary>
public sealed class CommandContext
{
    /// <summary>
    /// What the user is targeting. Usually the selection, but for keys it can be the hovered target.
    /// </summary>
    public IEnumerable<AtomRef> Selected
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<AtomRef>.Empty;

    /// <summary>
    /// Targets that are contained within the selection, like a selected deck's cards.
    /// Matching commands act on these when their <see cref="Command.ActsOn"/> includes them.
    /// </summary>
    public IEnumerable<AtomRef> Contents
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<AtomRef>.Empty;

    /// <summary>
    /// Targets that contain the selection, like a selected card's current deck.
    /// </summary>
    public IEnumerable<AtomRef> Containers
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<AtomRef>.Empty;

    /// <summary>
    /// What the selected targets refer to, like a component's prototype or data row.
    /// </summary>
    public IEnumerable<AtomRef> Referenced
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<AtomRef>.Empty;

    /// <summary>
    /// The atom the view shows, from its <see cref="ICommandView.Shown"/>, or empty.
    /// </summary>
    public IEnumerable<AtomRef> View
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<AtomRef>.Empty;

    /// <summary>
    /// Custom commands for this view to add to the context menu, like the table's Zoom to Component.
    /// </summary>
    public IReadOnlyList<Command> Local { get; init; } = [];

    /// <summary>
    /// <para>The full context of what's targeted in <paramref name="view"/>, read through <paramref name="R"/>.</para>
    ///
    /// The <see cref="CommandContext"/> is derived from the player's selection atoms.
    /// If there is no selection and <paramref name="keys"/> is true, the hovered target from <see cref="ICommandView.Hovered"/> is treated like the selection.
    /// The <see cref="Containers"/>, <see cref="Contents"/>, and <see cref="Referenced"/> are all derived from the selected targets.
    /// </summary>
    public static CommandContext Of(ICommandView view, IAtomReader R, bool keys = false)
    {
        if (view == null)
            return new();

        var selected =
            (view.SelectionScope(R) is { } place ? R.GetSelection(place)?.Targets : null)
            ?? ImmutableHashSet<AtomRef>.Empty;
        var chosen = selected.IsEmpty && keys ? view.Hovered().ToImmutableHashSet() : selected;

        IEnumerable<AtomRef> Related(
            IEnumerable<AtomRef> targets,
            Func<AtomRef, IEnumerable<AtomRef>> relation
        ) => targets.SelectMany(relation).Where(t => !chosen.Contains(t));

        // Each target is visited once, so if a relation ever does loop back it doesn't hang.
        HashSet<AtomRef> Followed(Func<AtomRef, IEnumerable<AtomRef>> relation)
        {
            var found = new HashSet<AtomRef>();
            for (
                var next = Related(chosen, relation).ToList();
                next.Count > 0;
                next = Related(next, relation).Where(t => !found.Contains(t)).ToList()
            )
                found.UnionWith(next);
            return found;
        }

        return new()
        {
            Selected = chosen,
            Contents = Followed(t => R.Resolve(t)?.Contents(R) ?? []),
            Containers = Followed(t => R.Resolve(t)?.Containers(R) ?? []),
            Referenced = Related(chosen, t => R.Resolve(t)?.Referenced(R) ?? []),
            View = view.Shown(R) is { } shown ? [shown] : [],
            Local = view.Commands,
        };
    }
}

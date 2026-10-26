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
    public IEnumerable<IAtomRef> Selected
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<IAtomRef>.Empty;

    /// <summary>
    /// Targets that are contained within the selection, like a selected deck's cards.
    /// Matching commands act on these when their <see cref="Command.ActsOn"/> includes them.
    /// </summary>
    public IEnumerable<IAtomRef> Contents
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<IAtomRef>.Empty;

    /// <summary>
    /// Targets that contain the selection, like a selected card's current deck.
    /// </summary>
    public IEnumerable<IAtomRef> Containers
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<IAtomRef>.Empty;

    /// <summary>
    /// What the selected targets refer to, like a component's prototype or data row.
    /// </summary>
    public IEnumerable<IAtomRef> Referenced
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<IAtomRef>.Empty;

    /// <summary>
    /// The atom the view is open on, the <see cref="ViewPlace.Subject"/> of its <see cref="ICommandView.Place"/>, or empty.
    /// </summary>
    public IEnumerable<IAtomRef> Subject
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<IAtomRef>.Empty;

    /// <summary>
    /// Custom commands for this view to add to the context menu, like the view's Zoom to Component.
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

        var selected = R.GetSelection(view)?.Targets ?? ImmutableHashSet<IAtomRef>.Empty;
        var chosen = selected.IsEmpty && keys ? view.Hovered().ToImmutableHashSet() : selected;

        IEnumerable<IAtomRef> Related(
            IEnumerable<IAtomRef> targets,
            Func<IAtomRef, IEnumerable<IAtomRef>> relation
        ) => targets.SelectMany(relation).Where(t => !chosen.Contains(t));

        // Each target is visited once, so if a relation ever does loop back it doesn't hang.
        HashSet<IAtomRef> Followed(Func<IAtomRef, IEnumerable<IAtomRef>> relation)
        {
            var found = new HashSet<IAtomRef>();
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
            Subject = view.Place(R)?.Subject is { } subject ? [subject] : [],
            Local = view.Commands,
        };
    }
}

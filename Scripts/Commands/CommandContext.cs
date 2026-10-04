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
    public IEnumerable<Target> Selected
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Targets that are contained within the selection, like a selected deck's cards.
    /// Matching commands act on these when their <see cref="Command.ActsOn"/> includes them.
    /// </summary>
    public IEnumerable<Target> Contents
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Targets that contain the selection, like a selected card's current deck.
    /// </summary>
    public IEnumerable<Target> Containers
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// What the selected targets refer to, like a component's prototype or data row.
    /// </summary>
    public IEnumerable<Target> Referenced
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// The record the view shows, from its <see cref="ICommandView.SelectionScope"/>, or empty.
    /// </summary>
    public IEnumerable<Target> View
    {
        get;
        init => field = value.ToImmutableHashSet();
    } = ImmutableHashSet<Target>.Empty;

    /// <summary>
    /// Custom commands for this view to add to the context menu, like the table's Zoom to Component.
    /// </summary>
    public IReadOnlyList<Command> Local { get; init; } = [];

    /// <summary>
    /// <para>The full context of what's targeted in <paramref name="view"/>, read through <paramref name="R"/>.</para>
    ///
    /// The <see cref="CommandContext"/> is derived from the player's selection records.
    /// If there is no selection and <paramref name="keys"/> is true, the hovered target from <see cref="ICommandView.Hovered"/> is treated like the selection.
    /// The <see cref="Containers"/>, <see cref="Contents"/>, and <see cref="Referenced"/> are all derived from the selected targets.
    /// </summary>
    public static CommandContext Of(ICommandView view, IRecordReader R, bool keys = false)
    {
        if (view == null)
            return new();

        bool scoped = view.SelectionScope.TryGet(R, out var scope);
        var selected = scoped
            ? R.GetSelection(scope)?.Targets ?? ImmutableHashSet<Target>.Empty
            : ImmutableHashSet<Target>.Empty;
        var chosen = selected.IsEmpty && keys ? view.Hovered().ToImmutableHashSet() : selected;

        IEnumerable<Target> Related(
            IEnumerable<Target> targets,
            Func<Target, IEnumerable<Target>> relation
        ) => targets.SelectMany(relation).Where(t => !chosen.Contains(t));

        // Each target is visited once, so if a relation ever does loop back it doesn't hang.
        HashSet<Target> Followed(Func<Target, IEnumerable<Target>> relation)
        {
            var found = new HashSet<Target>();
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
            Contents = Followed(t => t.Contents(R)),
            Containers = Followed(t => t.Containers(R)),
            Referenced = Related(chosen, t => t.Referenced(R)),
            View = scoped && scope != SnowTag.Empty ? [new RecordTarget(scope)] : [],
            Local = view.Commands,
        };
    }
}

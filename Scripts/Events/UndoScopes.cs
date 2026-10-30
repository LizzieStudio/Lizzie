using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// The scopes <see cref="UndoCommands"/> pass to <see cref="TimelineUndo.Undo"/> and <see cref="TimelineUndo.Redo"/>.
/// A unit is made where its first event says, in <see cref="TimelineEvent.Place"/>.
/// </summary>
public static class UndoScopes
{
    /// <summary>
    /// The player's own units made in <paramref name="place"/>.
    /// </summary>
    public static TimelineUndo.Scope Mine(byte me, ViewPlace place) =>
        (author, events) => author == me && events[0].Place == place;

    /// <summary>
    /// Other players' units made in <paramref name="place"/>.
    /// Ones that only change selections are passed over. That's too far.
    /// </summary>
    public static TimelineUndo.Scope Others(byte me, ViewPlace place) =>
        (author, events) =>
            author != me
            && events[0].Place == place
            && events.Any(e =>
                e.Writes.Any(atom => atom is not Selection)
                || e.Unsets.Any(unset => unset.Query.AtomType != typeof(Selection))
            );

    /// <summary>
    /// Anyone's units that change one of <paramref name="targets"/>, wherever they were made.
    /// An unset changes the atom with its id, or for a lookup, each target whose newest write before it matches.
    /// </summary>
    public static TimelineUndo.Scope Changing(Timeline timeline, IReadOnlySet<IAtomRef> targets) =>
        (_, events) =>
            events.Any(e =>
                e.Writes.Any(atom => targets.Contains(IAtomRef.To(atom)))
                || e.Unsets.Any(unset =>
                    unset.Query is IdQuery
                        ? targets.Contains((IAtomRef)unset.Key)
                        : targets.Any(target =>
                            WriteBefore(timeline, e.Id, target) is { } write && unset.Matches(write)
                        )
                )
            );

    private static Atom WriteBefore(Timeline timeline, SnowportId before, IAtomRef target) =>
        TimelineUndo
            .InEffect(timeline)
            .SkipWhile(e => e.Id.CompareTo(before) >= 0)
            .SelectMany(e => Enumerable.Reverse(e.Writes))
            .Where(TimelineUndo.Counts(timeline))
            .FirstOrDefault(atom => target.Equals(IAtomRef.To(atom)));
}

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
            && events.SelectMany(Changes).Any(atom => atom is not Selection);

    /// <summary>
    /// Anyone's units that change one of <paramref name="targets"/>, wherever they were made.
    /// An unset changes the atom it unsets, as the player unsetting it saw it.
    /// </summary>
    public static TimelineUndo.Scope Changing(IReadOnlySet<IAtomRef> targets) =>
        (_, events) => events.SelectMany(Changes).Any(atom => targets.Contains(IAtomRef.To(atom)));

    private static IEnumerable<Atom> Changes(TimelineEvent e) => e.Writes.Concat(e.Unsets);
}

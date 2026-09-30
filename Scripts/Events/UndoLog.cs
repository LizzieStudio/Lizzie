using System;
using System.Collections.Generic;
using Log = System.Collections.Generic.OrderedDictionary<SnowportId, TableEvent>;

/// <summary>
/// Undo and redo, worked out from the event log on every call.
/// </summary>
/// <remarks>
/// <para>
/// The EventLog is the source of truth. The current records are effectively just a cache for the EventLog.
/// When an Undo/Redo is published, it reverses a prior entry in the EventLog and makes as if it never happened.
/// These can be:
/// <list type="bullet">
/// <item>a regular event</item>
/// <item>a group of events (events pick their own group, like drag and drop)</item>
/// <item>another undo/redo (like how redo reverses an undo)</item>
/// </list>
/// </para>
/// <para>
/// If an event is reversed, it's as if it never happened, unless the reversal is reversed (redo), then the event is there.
/// This is worked out using an efficient backwards scan of the EventLog.
/// </para>
/// <para>
/// This system has very few requirements. You can undo an event older than the most recent one, and multiple undos can target the same event.
/// The only major requirement is that undo/redo events should be newer than their target, but with <see cref="SnowportId"/> that should be hard to screw up.
/// </para>
/// <para>
/// An Undo can reverse an Undo, but a Redo can only reverse an Undo. These are different things, and it has to do with "unfolding" a user's history.
/// If you Undo an action, then perform a new action, the undone action and the Undo action are both (effectively) added to your history.
/// Repeatedly hitting Undo will bring the undone action back and get rid of it again, so no history is lost.
/// An Undo/Redo pair don't have to unfold, though, since you reapplied that history anyways. Undo/Redo pairs (effectively) annihilate each other from your history.
/// </para>
/// <para>
/// I say effectively because the EventLog is never actually altered other than to add new events. These events live in the log forever; it's just treated this way.
/// </para>
/// <para>
/// Events can identify themselves as part of a "group", which "opens" with the first event that joins the group and "closes" when an event identifies itself as the last.
/// Groups are undone or redone together. This is used for gestures with multiple events, like drag and drop.
/// While groups are open, they're passed over by Undo actions.
/// </para>
/// <para>
/// When searching for the target for an undo or redo action, you can do so with a scope.
/// A scope is just a filter on the events, so that a valid target is selected from within the filter.
/// This is used extensively for many purposes:
/// <list type="bullet">
/// <item>The default "Undo Mine" event targets your own events.</item>
/// <item>The "Undo Others" event targets only the events that you didn't publish.</item>
/// <item>The "Undo This" event targets the last event that updated a particular record.</item>
/// <item>Panels and the Table (views) filter by the records pertinent to that view, like the DatasetEditor and DataSets</item>
/// </list>
/// </para>
/// </remarks>
public static class UndoLog
{
    /// <summary>
    /// Selects the target that Undo should submit, or null if there's nothing in scope to undo.
    /// </summary>
    public static UndoFlag Undo(Log log, byte me, Func<byte, Effect, bool> scope)
    {
        var reversed = new HashSet<SnowportId>();
        var targets = new Targets(log, me, scope);
        bool unfolding = false;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (e.Undo is { ByRedo: true } || !targets.Include(e, i))
                continue;
            if (e.Undo != null && e.Id.source == me && !unfolding)
                continue;

            unfolding = true;
            if (inEffect)
                return Reverse(log, e, byRedo: false);
        }

        return null;
    }

    /// <summary>
    /// Selects the target that Redo should submit, or null if there's nothing in scope to redo.
    /// </summary>
    public static UndoFlag Redo(Log log, byte me, Func<byte, Effect, bool> scope)
    {
        var reversed = new HashSet<SnowportId>();
        var targets = new Targets(log, me, scope);

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (!targets.Include(e, i))
                continue;

            if (e.Undo is not { } f || e.Id.source != me)
                return null;
            if (inEffect && !f.ByRedo)
                return Reverse(log, e, byRedo: true);
        }

        return null;
    }

    /// <summary>
    /// The flag that reverses <paramref name="e"/>'s history entry.
    /// </summary>
    private static UndoFlag Reverse(Log log, TableEvent e, bool byRedo)
    {
        var entry = Entry(e);
        List<SnowportId> also = null;
        if (IsReversal(log, entry, out var target))
        {
            var reversed = new HashSet<SnowportId>();
            for (int i = log.Count - 1; i >= 0; i--)
            {
                var other = log.GetAt(i).Value;
                if (other.Id.CompareTo(target.Reverses) <= 0)
                    break;
                if (
                    InEffect(reversed, other)
                    && other.Id != entry
                    && other.Undo is { } reversal
                    && reversal.Reverses.CompareTo(other.Id) < 0
                    && Reverses(reversal, target.Reverses)
                )
                    (also ??= new()).Add(other.Id);
            }
            also?.Reverse();
        }

        return new UndoFlag
        {
            Reverses = entry,
            ByRedo = byRedo,
            Also = also?.ToArray(),
        };
    }

    /// <summary>
    /// Whether <paramref name="reversal"/> reverses <paramref name="entry"/>.
    /// </summary>
    private static bool Reverses(UndoFlag reversal, SnowportId entry) =>
        reversal.Reverses == entry
        || (reversal.Also != null && Array.IndexOf(reversal.Also, entry) >= 0);

    /// <summary>
    /// An event's history entry: an event, a group, an undo id, or a redo id.
    /// </summary>
    private static SnowportId Entry(TableEvent e) => e.Undo == null ? e.Unit : e.Id;

    /// <summary>
    /// Call on every event while walking the log from newest to oldest.
    /// Returns true when the event is "in effect" and false when it was reversed.
    /// Uses <paramref name="reversed"/> to avoid needing to use any nested loops.
    /// </summary>
    private static bool InEffect(HashSet<SnowportId> reversed, TableEvent e)
    {
        if (reversed.Contains(Entry(e)))
            return false;
        if (e.Undo is { } f)
        {
            reversed.Add(f.Reverses);
            if (f.Also != null)
                reversed.UnionWith(f.Also);
        }
        return true;
    }

    /// <summary>
    /// The reversal with id <paramref name="entry"/>, if that's what the id is,
    /// and only if it reverses something older than itself, to avoid infinite loops.
    /// </summary>
    private static bool IsReversal(Log log, SnowportId entry, out UndoFlag reversal) =>
        (reversal = log.TryGetValue(entry, out var e) ? e.Undo : null) != null
        && reversal.Reverses.CompareTo(entry) < 0;

    /// <summary>
    /// The event or group an entry's chain of reversals comes down to.
    /// </summary>
    private static SnowportId UnitOf(Log log, SnowportId entry)
    {
        while (IsReversal(log, entry, out var reversal))
            entry = reversal.Reverses;
        return entry;
    }

    /// <summary>
    /// What Undo and Redo may target, judged once per entry over a walk from newest to oldest.
    /// </summary>
    private sealed class Targets(Log log, byte me, Func<byte, Effect, bool> scope)
    {
        // the index of each unit's newest event, for units the walk has met
        private readonly Dictionary<SnowportId, int> _newest = new();

        // whether each unit is closed
        private readonly Dictionary<SnowportId, bool> _closed = new();

        // whether each unit, as done by an author, changes something in scope
        private readonly Dictionary<(SnowportId, byte), bool> _inScope = new();

        // each entry's unit, and who it counts as done by
        private readonly Dictionary<SnowportId, (SnowportId Unit, byte Author)> _resolved = new();

        /// <summary>
        /// Whether the event at index <paramref name="i"/> is something to undo or redo:
        /// the unit its entry comes down to is closed, and changes something in scope as done by
        /// the entry's author. Call on every event in turn.
        /// </summary>
        public bool Include(TableEvent e, int i)
        {
            // The walk meets a unit first at its newest event, which says whether it's closed.
            if (e.Undo == null && _newest.TryAdd(e.Unit, i))
                _closed.TryAdd(e.Unit, e.Close || e.Group == SnowportId.Empty);

            var (unit, author) = Resolve(Entry(e));
            return IsClosed(unit, i) && InScope(unit, author, i);
        }

        /// <summary>
        /// The unit an entry comes down to, and who the entry counts as done by: an action by whoever
        /// started it, another player's reversal by them, and the player's own reversal by whoever did
        /// what it reverses.
        /// </summary>
        private (SnowportId Unit, byte Author) Resolve(SnowportId entry)
        {
            var chain = new List<(SnowportId Id, UndoFlag Reversal)>();
            var at = entry;
            while (!_resolved.ContainsKey(at) && IsReversal(log, at, out var reversal))
            {
                chain.Add((at, reversal));
                at = reversal.Reverses;
            }

            if (!_resolved.TryGetValue(at, out var resolved))
                _resolved[at] = resolved = (at, at.source);

            for (int k = chain.Count - 1; k >= 0; k--)
            {
                var id = chain[k].Id;
                resolved = (resolved.Unit, id.source == me ? resolved.Author : id.source);
                _resolved[id] = resolved;
            }
            return resolved;
        }

        /// <summary>
        /// All of a unit's events. Its newest is where the walk first met it,
        /// or for a unit not met yet, at or before index <paramref name="i"/>.
        /// </summary>
        private IEnumerable<TableEvent> AllMembers(SnowportId unit, int i) =>
            Members(log, unit, _newest.GetValueOrDefault(unit, i));

        /// <summary>
        /// Whether the unit is a single ungrouped event, or its newest event closes it.
        /// </summary>
        private bool IsClosed(SnowportId unit, int i)
        {
            if (_closed.TryGetValue(unit, out bool closed))
                return closed;

            TableEvent newest = null;
            foreach (var member in AllMembers(unit, i))
                newest = member;
            return _closed[unit] =
                newest != null && (newest.Close || newest.Group == SnowportId.Empty);
        }

        /// <summary>
        /// Whether any of the unit's events changes something in scope, as done by the author.
        /// </summary>
        private bool InScope(SnowportId unit, byte author, int i)
        {
            if (_inScope.TryGetValue((unit, author), out bool inScope))
                return inScope;

            foreach (var member in AllMembers(unit, i))
            foreach (var fx in member.Effects)
                if (scope(author, fx))
                    return _inScope[(unit, author)] = true;

            return _inScope[(unit, author)] = false;
        }
    }

    /// <summary>
    /// The groups started by <paramref name="source"/> that no event has closed yet, newest first.
    /// </summary>
    public static List<SnowportId> OpenGroups(Log log, byte source)
    {
        var open = new List<SnowportId>();
        var met = new HashSet<SnowportId>();

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            // a group's newest event says whether it's closed
            if (
                e.Group != SnowportId.Empty
                && met.Add(e.Group)
                && !e.Close
                && e.Group.source == source
            )
                open.Add(e.Group);
        }

        return open;
    }

    /// <summary>
    /// The events of <paramref name="unit"/> at or before index <paramref name="end"/>.
    /// None if <paramref name="unit"/> isn't a unit in the log.
    /// </summary>
    private static IEnumerable<TableEvent> Members(Log log, SnowportId unit, int end)
    {
        int start = log.IndexOf(unit);
        if (start < 0 || start > end)
            yield break;

        var first = log.GetAt(start).Value;
        if (first.Unit != unit)
            yield break;
        yield return first;

        // an ungrouped event is its own unit
        if (first.Group != unit)
            yield break;

        // A group's events all come after the event that names it.
        for (int j = start + 1; j <= end; j++)
        {
            var member = log.GetAt(j).Value;
            if (member.Unit == unit)
                yield return member;
        }
    }

    /// <summary>
    /// The events <paramref name="e"/> may have changed, for a store to recompute what they wrote.
    /// For a reversal, the events older than it of the unit its reversals come down to; for any other
    /// event, itself.
    /// </summary>
    public static IEnumerable<TableEvent> Changes(Log log, TableEvent e) =>
        e.Undo == null ? [e] : Members(log, UnitOf(log, e.Id), log.IndexOf(e.Id) - 1);

    /// <summary>
    /// The events whose history entry is in effect, newest first. For an action, that's not being
    /// undone, so the newest write to a record among these is what the record holds.
    /// </summary>
    public static IEnumerable<TableEvent> InEffect(Log log)
    {
        var reversed = new HashSet<SnowportId>();
        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            if (InEffect(reversed, e))
                yield return e;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Undo and redo, worked out from the timeline on every call.
/// </summary>
/// <remarks>
/// <para>
/// The Timeline is the source of truth. The current atoms are effectively just a cache for the Timeline.
/// When an Undo/Redo is published, it reverses a prior entry in the Timeline and makes as if it never happened.
/// These can be:
/// <list type="bullet">
/// <item>a regular event</item>
/// <item>a group of events (events pick their own group, like drag and drop)</item>
/// <item>another undo/redo (like how redo reverses an undo)</item>
/// </list>
/// </para>
/// <para>
/// If an event is reversed, it's as if it never happened, unless the reversal is reversed (redo), then the event is there.
/// This is worked out using an efficient backwards scan of the Timeline.
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
/// I say effectively because the Timeline is never actually altered other than to add new events. These events live in the timeline forever; it's just treated this way.
/// </para>
/// <para>
/// Events can identify themselves as part of a "group", which "opens" with the first event that joins the group and "closes" when an event identifies itself as the last.
/// Groups are undone or redone together. This is used for gestures with multiple events, like drag and drop.
/// While groups are open, they're passed over by Undo actions.
/// </para>
/// <para>
/// A gesture can end by canceling its group instead. A canceled group is treated as if it never happened.
/// The events in the group are reversed and Undo and Redo pass over it as if it weren't in the timeline.
/// </para>
/// <para>
/// Admin events (<see cref="TimelineEvent.IsAdmin"/>), like a loaded save, are never undone or redone.
/// They still count toward history, but they aren't owned by any player. They belong to the project.
/// </para>
/// <para>
/// When searching for the target for an undo or redo action, you can do so with a scope.
/// A scope is just a filter on the events, so that a valid target is selected from within the filter.
/// This is used extensively for many purposes:
/// <list type="bullet">
/// <item>The default "Undo Mine" event targets your own events.</item>
/// <item>The "Undo Others" event targets only the events that you didn't publish.</item>
/// <item>The "Undo This" event targets the last event that updated a particular atom.</item>
/// <item>Panels and the StageView filter by the atoms pertinent to that view, like the DatasetEditor and DataSets</item>
/// </list>
/// An unset is in scope when the atom it unsets is, as the player unsetting it saw it.
/// </para>
/// </remarks>
public static class TimelineUndo
{
    /// <summary>
    /// Selects the target that Undo should submit, or null if there's nothing in scope to undo.
    /// </summary>
    public static UndoFlag Undo(Timeline timeline, byte me, Func<byte, Atom, bool> scope)
    {
        var reversed = new HashSet<SnowportId>();
        var ends = new Ends(timeline);
        var targets = new Targets(timeline, me, scope, ends);
        bool unfolding = false;

        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (ends.Canceled(e, i))
                continue;
            if (e.Undo is { ByRedo: true } || !targets.Include(e, i) || e.IsAdmin)
                continue;
            if (e.Undo != null && e.Id.source == me && !unfolding)
                continue;

            unfolding = true;
            if (inEffect)
                return Reverse(timeline, e, byRedo: false);
        }

        return null;
    }

    /// <summary>
    /// Selects the target that Redo should submit, or null if there's nothing in scope to redo.
    /// </summary>
    public static UndoFlag Redo(Timeline timeline, byte me, Func<byte, Atom, bool> scope)
    {
        var reversed = new HashSet<SnowportId>();
        var ends = new Ends(timeline);
        var targets = new Targets(timeline, me, scope, ends);

        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (ends.Canceled(e, i))
                continue;
            if (!targets.Include(e, i) || e.IsAdmin)
                continue;

            if (e.Undo is not { } f || e.Id.source != me)
                return null;
            if (inEffect && !f.ByRedo)
                return Reverse(timeline, e, byRedo: true);
        }

        return null;
    }

    /// <summary>
    /// The flag that reverses <paramref name="e"/>'s history entry.
    /// </summary>
    private static UndoFlag Reverse(Timeline timeline, TimelineEvent e, bool byRedo)
    {
        var entry = Entry(e);
        List<SnowportId> also = null;
        if (IsReversal(timeline, entry, out var target))
        {
            var reversed = new HashSet<SnowportId>();
            for (int i = timeline.Count - 1; i >= 0; i--)
            {
                var other = timeline.GetAt(i).Value;
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
    private static SnowportId Entry(TimelineEvent e) => e.Undo == null ? e.Unit : e.Id;

    /// <summary>
    /// Call on every event while walking the timeline from newest to oldest.
    /// Returns true when the event is "in effect" and false when it was reversed.
    /// Uses <paramref name="reversed"/> to avoid needing to use any nested loops.
    /// </summary>
    private static bool InEffect(HashSet<SnowportId> reversed, TimelineEvent e)
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
    private static bool IsReversal(Timeline timeline, SnowportId entry, out UndoFlag reversal) =>
        (reversal = timeline.TryGetValue(entry, out var e) ? e.Undo : null) != null
        && reversal.Reverses.CompareTo(entry) < 0;

    /// <summary>
    /// The event or group an entry's chain of reversals comes down to.
    /// </summary>
    private static SnowportId UnitOf(Timeline timeline, SnowportId entry)
    {
        while (IsReversal(timeline, entry, out var reversal))
            entry = reversal.Reverses;
        return entry;
    }

    /// <summary>
    /// What Undo and Redo may target, judged once per entry over a walk from newest to oldest.
    /// </summary>
    private sealed class Targets(
        Timeline timeline,
        byte me,
        Func<byte, Atom, bool> scope,
        Ends ends
    )
    {
        // the index of each unit's newest event, for units the walk has met
        private readonly Dictionary<SnowportId, int> _newest = new();

        // whether each unit, as done by an author, changes something in scope
        private readonly Dictionary<(SnowportId, byte), bool> _inScope = new();

        // each entry's unit, and who it counts as done by
        private readonly Dictionary<SnowportId, (SnowportId Unit, byte Author)> _resolved = new();

        /// <summary>
        /// Whether the event at index <paramref name="i"/> is something to undo or redo:
        /// the unit its entry comes down to is closed, and changes something in scope as done by
        /// the entry's author. Call on every event in turn, after <see cref="Ends.Canceled"/>.
        /// </summary>
        public bool Include(TimelineEvent e, int i)
        {
            if (e.Undo == null)
                _newest.TryAdd(e.Unit, i);

            var (unit, author) = Resolve(Entry(e));
            return ends.Of(unit, i) != GroupEnd.None && InScope(unit, author, i);
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
            while (!_resolved.ContainsKey(at) && IsReversal(timeline, at, out var reversal))
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
        private IEnumerable<TimelineEvent> AllMembers(SnowportId unit, int i) =>
            Members(timeline, unit, _newest.GetValueOrDefault(unit, i));

        /// <summary>
        /// Whether any of the unit's events changes something in scope, as done by the author.
        /// </summary>
        private bool InScope(SnowportId unit, byte author, int i)
        {
            if (_inScope.TryGetValue((unit, author), out bool inScope))
                return inScope;

            foreach (var member in AllMembers(unit, i))
            foreach (var atom in member.Writes.Concat(member.Unsets))
                if (scope(author, atom))
                    return _inScope[(unit, author)] = true;

            return _inScope[(unit, author)] = false;
        }
    }

    /// <summary>
    /// How each unit ended, worked out once per unit over a walk from newest to oldest.
    /// <list type="bullet">
    /// <item>An ungrouped event is <see cref="GroupEnd.Closed"/>.</item>
    /// <item>A group ends as its newest event says, which is <see cref="GroupEnd.None"/> while it's open.</item>
    /// <item>A cancel after an earlier event ended the group does nothing, so the group counts as closed.</item>
    /// </list>
    /// </summary>
    private sealed class Ends(Timeline timeline)
    {
        private readonly Dictionary<SnowportId, GroupEnd> _ends = new();

        /// <summary>
        /// Whether the event at index <paramref name="i"/> was canceled,
        /// which can happen if it's in a canceled group or it reverses a canceled group.
        /// Call on every event in turn.
        /// </summary>
        public bool Canceled(TimelineEvent e, int i)
        {
            if (e.Undo != null)
                return Of(UnitOf(timeline, e.Id), i) == GroupEnd.Canceled;

            // The walk meets a unit first at its newest event, which says how it ended.
            // Only a cancel needs the older events.
            if (!_ends.TryGetValue(e.Unit, out var end))
                _ends[e.Unit] = end = Decide(
                    e,
                    e.End == GroupEnd.Canceled && EndedBefore(e.Unit, i)
                );
            return end == GroupEnd.Canceled;
        }

        /// <summary>
        /// How <paramref name="unit"/> ended.
        /// </summary>
        public GroupEnd Of(SnowportId unit, int i)
        {
            if (_ends.TryGetValue(unit, out var end))
                return end;

            TimelineEvent newest = null;
            bool endedBefore = false;
            foreach (var member in Members(timeline, unit, i))
            {
                endedBefore |= newest is { End: not GroupEnd.None };
                newest = member;
            }
            return _ends[unit] = newest == null ? GroupEnd.None : Decide(newest, endedBefore);
        }

        // Whether an event of the unit before index i ended it.
        private bool EndedBefore(SnowportId unit, int i)
        {
            foreach (var member in Members(timeline, unit, i - 1))
                if (member.End != GroupEnd.None)
                    return true;
            return false;
        }

        // How a unit ended, given its newest event and whether an older one already ended it.
        private static GroupEnd Decide(TimelineEvent newest, bool endedBefore) =>
            newest.Group == SnowportId.Empty ? GroupEnd.Closed
            : newest.End == GroupEnd.Canceled && endedBefore ? GroupEnd.Closed
            : newest.End;
    }

    /// <summary>
    /// Whether <paramref name="group"/> is in the timeline and no event has closed or canceled it yet.
    /// </summary>
    public static bool IsOpen(Timeline timeline, SnowportId group)
    {
        TimelineEvent newest = null;
        foreach (var member in Members(timeline, group, timeline.Count - 1))
            newest = member;
        return newest is { End: GroupEnd.None } && newest.Group == group;
    }

    /// <summary>
    /// The groups started by <paramref name="source"/> that no event has closed yet, newest first.
    /// </summary>
    public static List<SnowportId> OpenGroups(Timeline timeline, byte source)
    {
        var open = new List<SnowportId>();
        var met = new HashSet<SnowportId>();

        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            // a group's newest event says whether it's closed
            if (
                e.Group != SnowportId.Empty
                && met.Add(e.Group)
                && e.End == GroupEnd.None
                && e.Group.source == source
            )
                open.Add(e.Group);
        }

        return open;
    }

    /// <summary>
    /// The events of <paramref name="unit"/> at or before index <paramref name="end"/>.
    /// None if <paramref name="unit"/> isn't a unit in the timeline.
    /// </summary>
    private static IEnumerable<TimelineEvent> Members(Timeline timeline, SnowportId unit, int end)
    {
        int start = timeline.IndexOf(unit);
        if (start < 0 || start > end)
            yield break;

        var first = timeline.GetAt(start).Value;
        if (first.Unit != unit)
            yield break;
        yield return first;

        // an ungrouped event is its own unit
        if (first.Group != unit)
            yield break;

        // A group's events all come after the event that names it.
        for (int j = start + 1; j <= end; j++)
        {
            var member = timeline.GetAt(j).Value;
            if (member.Unit == unit)
                yield return member;
        }
    }

    /// <summary>
    /// The events <paramref name="e"/> may have changed, for a store to recompute what they wrote.
    /// <list type="bullet">
    /// <item>For a reversal, the events older than it of the unit its reversals come down to.</item>
    /// <item>
    /// For an event that ends a group, or one in a group that was canceled, the group's events up to it,
    /// since how a group ends decides whether it's canceled.
    /// </item>
    /// <item>For any other event, itself.</item>
    /// </list>
    /// </summary>
    public static IEnumerable<TimelineEvent> Changes(Timeline timeline, TimelineEvent e)
    {
        if (e.Undo != null)
            return Members(timeline, UnitOf(timeline, e.Id), timeline.IndexOf(e.Id) - 1);
        if (e.Group == SnowportId.Empty)
            return [e];

        var group = new List<TimelineEvent>(Members(timeline, e.Group, timeline.IndexOf(e.Id)));
        return e.End != GroupEnd.None || group.Exists(m => m.End == GroupEnd.Canceled)
            ? group
            : [e];
    }

    /// <summary>
    /// The events whose history entry is in effect, newest first. For an action, that's not being
    /// undone, so the newest write to an atom among these is what the atom holds.
    /// </summary>
    public static IEnumerable<TimelineEvent> InEffect(Timeline timeline)
    {
        var reversed = new HashSet<SnowportId>();
        var ends = new Ends(timeline);
        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (!ends.Canceled(e, i) && inEffect)
                yield return e;
        }
    }
}

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
/// A <see cref="Transition"/>'s writes are drafts, which only count until a closing event <see cref="TimelineEvent.Ends"/> it.
/// The closing event holds all of it that's kept, and undoing the transition means undoing its closing event.
/// An event that writes nothing except drafts is skipped by undo/redo.
/// </para>
/// <para>
/// Admin events (<see cref="TimelineEvent.IsAdmin"/>), like a loaded save, are never undone or redone.
/// They still count toward history, but they aren't owned by any player. They belong to the project.
/// </para>
/// <para>
/// When searching for the target for an undo or redo action, you can do so with a scope.
/// A scope is a filter on events, so that a valid target is selected from within the filter.
/// <see cref="UndoScopes"/> has the ones the undo commands use:
/// <list type="bullet">
/// <item>"Undo Mine" targets your own events made in the view's place.</item>
/// <item>"Undo Others" targets the events other players made in the view's place.</item>
/// <item>"Undo This" targets the last event that changed a particular atom.</item>
/// </list>
/// </para>
/// </remarks>
public static class TimelineUndo
{
    /// <summary>
    /// Whether an action, as done by <paramref name="author"/>, is something to undo or redo.
    /// </summary>
    public delegate bool Scope(byte author, TimelineEvent action);

    /// <summary>
    /// Selects the target that Undo should submit, or null if there's nothing in scope to undo.
    /// </summary>
    public static UndoFlag Undo(Timeline timeline, byte me, Scope scope)
    {
        var reversed = new HashSet<SnowportId>();
        var targets = new Targets(timeline, me, scope);
        bool unfolding = false;

        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (e.Undo is { ByRedo: true } || !targets.Include(e) || e.IsAdmin)
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
    public static UndoFlag Redo(Timeline timeline, byte me, Scope scope)
    {
        var reversed = new HashSet<SnowportId>();
        var targets = new Targets(timeline, me, scope);

        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            bool inEffect = InEffect(reversed, e);
            if (!targets.Include(e) || e.IsAdmin)
                continue;

            if (e.Undo is not { } f || e.Id.source != me)
                return null;
            if (inEffect && !f.ByRedo)
                return Reverse(timeline, e, byRedo: true);
        }

        return null;
    }

    /// <summary>
    /// The flag that reverses <paramref name="e"/>.
    /// </summary>
    private static UndoFlag Reverse(Timeline timeline, TimelineEvent e, bool byRedo)
    {
        List<SnowportId> also = null;
        if (IsReversal(timeline, e.Id, out var target))
        {
            var reversed = new HashSet<SnowportId>();
            for (int i = timeline.Count - 1; i >= 0; i--)
            {
                var other = timeline.GetAt(i).Value;
                if (other.Id.CompareTo(target.Reverses) <= 0)
                    break;
                if (
                    InEffect(reversed, other)
                    && other.Id != e.Id
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
            Reverses = e.Id,
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
    /// Call on every event while walking the timeline from newest to oldest.
    /// Returns true when the event is "in effect" and false when it was reversed.
    /// Uses <paramref name="reversed"/> to avoid needing to use any nested loops.
    /// </summary>
    private static bool InEffect(HashSet<SnowportId> reversed, TimelineEvent e)
    {
        if (reversed.Contains(e.Id))
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
    /// The event an entry's chain of reversals comes down to.
    /// </summary>
    private static SnowportId ActionOf(Timeline timeline, SnowportId entry)
    {
        while (IsReversal(timeline, entry, out var reversal))
            entry = reversal.Reverses;
        return entry;
    }

    /// <summary>
    /// What Undo and Redo may target, judged once per entry over a walk from newest to oldest.
    /// </summary>
    private sealed class Targets(Timeline timeline, byte me, Scope scope)
    {
        // whether each action, as done by an author, is in scope
        private readonly Dictionary<(SnowportId, byte), bool> _inScope = new();

        // each entry's action, and who it counts as done by
        private readonly Dictionary<SnowportId, (SnowportId Action, byte Author)> _resolved = new();

        /// <summary>
        /// Whether <paramref name="e"/> is something to undo or redo:
        /// the action it comes down to commits something, and is in scope as done by its author.
        /// </summary>
        public bool Include(TimelineEvent e)
        {
            var (action, author) = Resolve(e.Id);
            return timeline.TryGetValue(action, out var done)
                && Commits(done)
                && InScope(done, author);
        }

        /// <summary>
        /// The action an entry comes down to, and who the entry counts as done by: an action by whoever
        /// did it, another player's reversal by them, and the player's own reversal by whoever did
        /// what it reverses.
        /// </summary>
        private (SnowportId Action, byte Author) Resolve(SnowportId entry)
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
                resolved = (resolved.Action, id.source == me ? resolved.Author : id.source);
                _resolved[id] = resolved;
            }
            return resolved;
        }

        /// <summary>
        /// Whether the action is in scope, as done by the author.
        /// </summary>
        private bool InScope(TimelineEvent action, byte author)
        {
            if (!_inScope.TryGetValue((action.Id, author), out bool inScope))
                _inScope[(action.Id, author)] = inScope = scope(author, action);
            return inScope;
        }
    }

    /// <summary>
    /// The transitions started by <paramref name="source"/> that no event has ended yet.
    /// </summary>
    public static List<Transition> OpenTransitions(Timeline timeline, byte source)
    {
        var ended = timeline.Values.Select(e => e.Ends).ToHashSet();
        return timeline
            .Values.Where(e =>
                e.Id.source == source
                && Array.Exists(e.Writes, write => write.Transition.Start == e.Id)
            )
            .Select(e => new Transition(e.Id))
            .Where(transition => !ended.Contains(transition))
            .ToList();
    }

    /// <summary>
    /// The events <paramref name="e"/> may have changed, for a store to recompute what they wrote.
    /// <list type="bullet">
    /// <item>For a reversal, the event its chain of reversals comes down to.</item>
    /// <item>For an event that ends a transition, itself and every event with a draft of the transition, since its drafts stop counting.</item>
    /// <item>For any other event, itself.</item>
    /// </list>
    /// </summary>
    public static IEnumerable<TimelineEvent> Changes(Timeline timeline, TimelineEvent e)
    {
        if (e.Undo != null)
        {
            var action = ActionOf(timeline, e.Id);
            return action != e.Id && timeline.TryGetValue(action, out var reversed)
                ? [reversed]
                : [];
        }
        if (e.Ends != Transition.None)
            return Drafting(timeline, e.Ends).Prepend(e);
        return [e];
    }

    /// <summary>
    /// The events with a draft of <paramref name="transition"/>.
    /// </summary>
    private static IEnumerable<TimelineEvent> Drafting(Timeline timeline, Transition transition)
    {
        int start = timeline.IndexOf(transition.Start);
        if (start < 0)
            yield break;
        for (int j = start; j < timeline.Count; j++)
        {
            var e = timeline.GetAt(j).Value;
            if (Array.Exists(e.Writes, write => write.Transition == transition))
                yield return e;
        }
    }

    /// <summary>
    /// Whether an event ends <paramref name="transition"/>, wherever it sorts after the transition's first event.
    /// What happens to that event later doesn't matter.
    /// </summary>
    public static bool Ended(Timeline timeline, Transition transition)
    {
        int start = timeline.IndexOf(transition.Start);
        if (start < 0)
            return false;
        for (int j = start; j < timeline.Count; j++)
            if (timeline.GetAt(j).Value.Ends == transition)
                return true;
        return false;
    }

    /// <summary>
    /// Whether a write counts toward what its atom holds.
    ///
    /// <para>
    /// A committed write always does, and a draft only until its transition <see cref="Ended"/>.
    /// The predicate looks each transition up once.
    /// </para>
    /// </summary>
    public static Func<Atom, bool> Counts(Timeline timeline)
    {
        var ended = new Dictionary<Transition, bool>();
        return write =>
            write.Transition == Transition.None
            || !(
                ended.TryGetValue(write.Transition, out bool known)
                    ? known
                    : ended[write.Transition] = Ended(timeline, write.Transition)
            );
    }

    /// <summary>
    /// Whether an action commits anything, so there's something to undo:
    /// it unsets something, or writes something that isn't a draft.
    /// </summary>
    private static bool Commits(TimelineEvent e) =>
        e.Unsets.Length > 0 || Array.Exists(e.Writes, write => write.Transition == Transition.None);

    /// <summary>
    /// The events whose history entry is in effect, newest first. For an action, that's not being
    /// undone, so the newest write to an atom among these is what the atom holds.
    /// </summary>
    public static IEnumerable<TimelineEvent> InEffect(Timeline timeline)
    {
        var reversed = new HashSet<SnowportId>();
        for (int i = timeline.Count - 1; i >= 0; i--)
        {
            var e = timeline.GetAt(i).Value;
            if (InEffect(reversed, e))
                yield return e;
        }
    }
}

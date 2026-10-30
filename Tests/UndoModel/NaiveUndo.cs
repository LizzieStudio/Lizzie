using System;
using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

namespace Lizzie.Tests;

/// <summary>
/// The undo rules written as plainly as possible, as the model the real implementations are checked against.
/// Every answer comes from scanning the whole timeline, so it's slow, but each rule reads as its definition.
/// </summary>
public sealed class NaiveUndo : IUndoApi
{
    public string Name => "naive";

    #region The rules

    private static bool Newer(TimelineEvent a, TimelineEvent b) => a.Id.CompareTo(b.Id) > 0;

    private static IEnumerable<TimelineEvent> NewestFirst(Timeline timeline) =>
        timeline.Values.Reverse();

    /// <summary>The reversal with this id, if the entry is one. A reversal only reverses something older than itself.</summary>
    private static TimelineEvent ReversalAt(Timeline timeline, SnowportId entry) =>
        timeline.Values.FirstOrDefault(e =>
            e.Id == entry && e.Undo != null && e.Undo.Reverses.CompareTo(e.Id) < 0
        );

    /// <summary>Every entry a reversal reverses: its own, and the other reversals it retires with it.</summary>
    private static IEnumerable<SnowportId> Reversed(UndoFlag f) =>
        (f.Also ?? []).Prepend(f.Reverses);

    /// <summary>
    /// An event's history entry is in effect unless a newer reversal, itself in effect, reverses it.
    /// </summary>
    private static bool InEffect(Timeline timeline, TimelineEvent e) =>
        !timeline.Values.Any(reversal =>
            reversal.Undo != null
            && Reversed(reversal.Undo).Contains(e.Id)
            && Newer(reversal, e)
            && InEffect(timeline, reversal)
        );

    /// <summary>
    /// History is the only source of truth: an event is undone when its entry isn't in effect.
    /// The same as the interface's, asked directly.
    /// </summary>
    public bool IsUndone(Timeline timeline, TimelineEvent e) => !InEffect(timeline, e);

    /// <summary>The action an entry comes down to: itself, or for a reversal, that of what it reverses.</summary>
    public static SnowportId ActionOf(Timeline timeline, SnowportId entry) =>
        ReversalAt(timeline, entry) is { } reversal
            ? ActionOf(timeline, reversal.Undo.Reverses)
            : entry;

    /// <summary>
    /// Who an entry counts as done by, for the player <paramref name="me"/>: an action by whoever did it,
    /// another player's reversal by them, and the player's own reversal by whoever did what it reverses.
    /// </summary>
    private static byte AuthorOf(Timeline timeline, SnowportId entry, byte me) =>
        ReversalAt(timeline, entry) is not { } reversal ? entry.source
        : reversal.Id.source == me ? AuthorOf(timeline, reversal.Undo.Reverses, me)
        : reversal.Id.source;

    /// <summary>
    /// An action commits something when it unsets something or writes something that isn't a draft.
    /// The rest, like a transition in progress or its cancel, is nothing to undo.
    /// </summary>
    public static bool Commits(TimelineEvent action) =>
        action.Unsets.Length > 0 || action.Writes.Any(w => w.Transition == Transition.None);

    /// <summary>
    /// Whether Undo or Redo may act on the event: it isn't an admin event, the action its entry
    /// comes down to commits something, and it's in scope as done by the entry's author.
    /// </summary>
    private static bool IsCandidate(
        Timeline timeline,
        TimelineEvent e,
        byte me,
        TimelineUndo.Scope scope
    ) =>
        !e.IsAdmin
        && timeline.TryGetValue(ActionOf(timeline, e.Id), out var action)
        && Commits(action)
        && scope(AuthorOf(timeline, e.Id, me), action);

    /// <summary>
    /// The events a reversal may change: the action its entry comes down to, if it's older than the reversal.
    /// </summary>
    private static IEnumerable<TimelineEvent> CoveredBy(
        Timeline timeline,
        TimelineEvent reversal
    ) => timeline.Values.Where(e => e.Id == ActionOf(timeline, reversal.Id) && Newer(reversal, e));

    #endregion

    #region Undo and redo

    /// <summary>
    /// Undo walks back through history. The player's latest undo and redo reversals are passed over,
    /// so each reversal goes one step further back. Past them everything is history, the player's own
    /// older undo reversals included, and the newest entry still in effect is reversed.
    /// A Redo reversal is never history: it cancels out with the Undo reversal it reversed.
    /// </summary>
    public UndoFlag Undo(Timeline timeline, byte me, TimelineUndo.Scope scope)
    {
        bool pastLatestReversals = false;
        foreach (var e in NewestFirst(timeline))
        {
            if (e.Undo is { ByRedo: true } || !IsCandidate(timeline, e, me, scope))
                continue;
            if (e.Undo != null && e.Id.source == me && !pastLatestReversals)
                continue;
            pastLatestReversals = true;
            if (InEffect(timeline, e))
                return Reverse(timeline, e);
        }
        return null;
    }

    /// <summary>
    /// The reversal that reverses an event's history entry. When that entry is itself a reversal, the other
    /// reversals still in effect on the entry it reversed are retired too, so that comes back whoever else
    /// reversed it as well.
    /// </summary>
    private static UndoFlag Reverse(Timeline timeline, TimelineEvent e, bool byRedo = false)
    {
        var also = ReversalAt(timeline, e.Id) is { } target
            ? timeline
                .Values.Where(other =>
                    other.Id != e.Id
                    && ReversalAt(timeline, other.Id) != null
                    && Reversed(other.Undo).Contains(target.Undo.Reverses)
                    && InEffect(timeline, other)
                )
                .Select(other => other.Id)
                .ToArray()
            : [];
        return new()
        {
            Reverses = e.Id,
            ByRedo = byRedo,
            Also = also.Length > 0 ? also : null,
        };
    }

    /// <summary>
    /// Redo reverses the player's newest Undo reversal still in effect, among their latest reversals.
    /// Anything else that has happened since leaves nothing to redo.
    /// </summary>
    public UndoFlag Redo(Timeline timeline, byte me, TimelineUndo.Scope scope)
    {
        foreach (var e in NewestFirst(timeline))
        {
            if (!IsCandidate(timeline, e, me, scope))
                continue;
            if (e.Undo == null || e.Id.source != me)
                return null;
            if (!e.Undo.ByRedo && InEffect(timeline, e))
                return Reverse(timeline, e, byRedo: true);
        }
        return null;
    }

    #endregion

    #region Transitions and changes

    /// <summary>
    /// The transitions a player started that no event has ended. A transition is started by an event
    /// with a draft of itself.
    /// </summary>
    public List<Transition> OpenTransitions(Timeline timeline, byte source) =>
        timeline
            .Values.Where(e =>
                e.Id.source == source && e.Writes.Any(w => w.Transition == new Transition(e.Id))
            )
            .Select(e => new Transition(e.Id))
            .Where(transition => !timeline.Values.Any(e => e.Ends == transition))
            .ToList();

    /// <summary>
    /// What a reversal may change is what it covers. An event that ends a transition may change itself and
    /// every event with a draft of the transition, since its drafts stop counting.
    /// Any other event changes only itself.
    /// </summary>
    public IEnumerable<TimelineEvent> Changes(Timeline timeline, TimelineEvent e)
    {
        if (e.Undo != null)
            return CoveredBy(timeline, e);
        if (e.Ends != Transition.None)
            return timeline.Values.Where(d => d == e || d.Writes.Any(w => w.Transition == e.Ends));
        return [e];
    }

    public IEnumerable<TimelineEvent> InEffect(Timeline timeline) =>
        NewestFirst(timeline).Where(e => InEffect(timeline, e));

    #endregion

    #region What atoms hold

    /// <summary>
    /// A write counts while it isn't a draft, or its transition hasn't been ended by any event, whatever became of that event.
    /// </summary>
    private static bool Counts(Timeline timeline, Atom write) =>
        write.Transition == Transition.None
        || !timeline.Values.Any(e => e.Ends == write.Transition);

    /// <summary>Whether <paramref name="unset"/> unsets <paramref name="write"/>.</summary>
    private static bool Takes(Unset unset, TestAtom write) =>
        unset.Query == TestAtom.All || Equals(unset.Key, write.Id);

    /// <summary>
    /// What an atom holds: its newest write that counts, among the events in effect,
    /// unless an event in effect that's newer than it unsets it. Null for nothing.
    /// </summary>
    public static TestAtom Holds(Timeline timeline, Ref<TestAtom> atom)
    {
        TestAtom Write(TimelineEvent e) =>
            e
                .Writes.OfType<TestAtom>()
                .LastOrDefault(w => w.Id.Equals(atom) && Counts(timeline, w));

        var written = NewestFirst(timeline)
            .FirstOrDefault(e => InEffect(timeline, e) && Write(e) != null);
        if (written == null)
            return null;
        var write = Write(written);
        bool unset = timeline.Values.Any(u =>
            Newer(u, written) && InEffect(timeline, u) && u.Unsets.Any(x => Takes(x, write))
        );
        return unset ? null : write;
    }

    #endregion
}

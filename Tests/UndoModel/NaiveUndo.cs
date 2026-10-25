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

    /// <summary>
    /// The events of a unit. A unit is named by its first event: a group's first event names itself
    /// as the group, and an ungrouped event is a unit of one. Any other id names nothing.
    /// </summary>
    private static List<TimelineEvent> Members(Timeline timeline, SnowportId unit)
    {
        var first = timeline.Values.FirstOrDefault(e => e.Id == unit);
        if (first == null || first.Unit != unit)
            return [];
        if (first.Group == SnowportId.Empty)
            return [first];
        return timeline.Values.Where(e => e.Group == unit).ToList();
    }

    /// <summary>An event's history entry: an action's unit, or a reversal's own id.</summary>
    private static SnowportId Entry(TimelineEvent e) => e.Undo == null ? e.Unit : e.Id;

    /// <summary>The reversal with this id, if the entry is one. A reversal only reverses something older than itself.</summary>
    private static TimelineEvent ReversalAt(Timeline timeline, SnowportId entry) =>
        timeline.Values.FirstOrDefault(e =>
            e.Id == entry && e.Undo != null && e.Undo.Reverses.CompareTo(e.Id) < 0
        );

    /// <summary>Every entry a reversal reverses: its own, and the other reversals it retires with it.</summary>
    private static IEnumerable<SnowportId> Reversed(UndoFlag f) =>
        (f.Also ?? []).Prepend(f.Reverses);

    /// <summary>
    /// A group is canceled when its newest event cancels it and no event before that closed it.
    /// </summary>
    public static bool IsCanceled(Timeline timeline, SnowportId group)
    {
        var members = Members(timeline, group);
        if (members.Count == 0 || members[0].Group != group)
            return false;
        var newest = members.MaxBy(e => e.Id);
        return newest.End == GroupEnd.Canceled
            && !members.Any(m => m != newest && m.End != GroupEnd.None);
    }

    /// <summary>
    /// The events of a canceled group, its cancel included, never happened, so they aren't history.
    /// Nor did a reversal that comes down to one.
    /// </summary>
    private static bool NeverHappened(Timeline timeline, TimelineEvent e) =>
        IsCanceled(timeline, e.Undo == null ? e.Unit : UnitOf(timeline, e.Id));

    /// <summary>
    /// An event's history entry is in effect unless it never happened, or a newer reversal, itself in effect, reverses it.
    /// </summary>
    private static bool InEffect(Timeline timeline, TimelineEvent e) =>
        !NeverHappened(timeline, e)
        && !timeline.Values.Any(reversal =>
            reversal.Undo != null
            && Reversed(reversal.Undo).Contains(Entry(e))
            && Newer(reversal, e)
            && InEffect(timeline, reversal)
        );

    /// <summary>
    /// History is the only source of truth: an event is undone when its entry isn't in effect.
    /// The same as the interface's, asked directly.
    /// </summary>
    public bool IsUndone(Timeline timeline, TimelineEvent e) => !InEffect(timeline, e);

    /// <summary>The unit an entry comes down to: an action's own, or for a reversal, that of what it reverses.</summary>
    public static SnowportId UnitOf(Timeline timeline, SnowportId entry) =>
        ReversalAt(timeline, entry) is { } reversal
            ? UnitOf(timeline, reversal.Undo.Reverses)
            : entry;

    /// <summary>
    /// Who an entry counts as done by, for the player <paramref name="me"/>: an action by whoever started it,
    /// another player's reversal by them, and the player's own reversal by whoever did what it reverses.
    /// </summary>
    private static byte AuthorOf(Timeline timeline, SnowportId entry, byte me) =>
        ReversalAt(timeline, entry) is not { } reversal ? entry.source
        : reversal.Id.source == me ? AuthorOf(timeline, reversal.Undo.Reverses, me)
        : reversal.Id.source;

    /// <summary>
    /// A unit is finished when it's a single ungrouped event, or when its newest event closes it.
    /// Until then it's a gesture in progress.
    /// </summary>
    public static bool IsClosed(Timeline timeline, SnowportId unit)
    {
        var members = Members(timeline, unit);
        if (members.Count == 0)
            return false;
        if (members[0].Group == SnowportId.Empty)
            return true;
        return members.MaxBy(e => e.Id).End != GroupEnd.None;
    }

    /// <summary>Whether the unit's events are in scope, as done by the author.</summary>
    private static bool InScope(
        Timeline timeline,
        SnowportId unit,
        byte author,
        TimelineUndo.Scope scope
    ) => scope(author, Members(timeline, unit));

    /// <summary>
    /// Whether Undo or Redo may act on the event: it isn't an admin event, the unit its entry
    /// comes down to is finished, and in scope as done by the entry's author.
    /// </summary>
    private static bool IsCandidate(
        Timeline timeline,
        TimelineEvent e,
        byte me,
        TimelineUndo.Scope scope
    )
    {
        if (e.IsAdmin)
            return false;
        var unit = UnitOf(timeline, Entry(e));
        return IsClosed(timeline, unit)
            && InScope(timeline, unit, AuthorOf(timeline, Entry(e), me), scope);
    }

    /// <summary>
    /// The events a reversal may change: those of the unit its entry comes down to, older than the reversal.
    /// </summary>
    private static IEnumerable<TimelineEvent> CoveredBy(
        Timeline timeline,
        TimelineEvent reversal
    ) => Members(timeline, UnitOf(timeline, reversal.Id)).Where(m => Newer(reversal, m));

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
            if (NeverHappened(timeline, e))
                continue;
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
        var entry = Entry(e);
        var also = ReversalAt(timeline, entry) is { } target
            ? timeline
                .Values.Where(other =>
                    other.Id != entry
                    && ReversalAt(timeline, other.Id) != null
                    && Reversed(other.Undo).Contains(target.Undo.Reverses)
                    && InEffect(timeline, other)
                )
                .Select(other => other.Id)
                .ToArray()
            : [];
        return new()
        {
            Reverses = entry,
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
            if (NeverHappened(timeline, e) || !IsCandidate(timeline, e, me, scope))
                continue;
            if (e.Undo == null || e.Id.source != me)
                return null;
            if (!e.Undo.ByRedo && InEffect(timeline, e))
                return Reverse(timeline, e, byRedo: true);
        }
        return null;
    }

    #endregion

    #region Groups and changes

    /// <summary>The groups a player started whose newest event doesn't close them.</summary>
    public List<SnowportId> OpenGroups(Timeline timeline, byte source) =>
        timeline
            .Values.Where(e => e.Group != SnowportId.Empty && e.Group.source == source)
            .GroupBy(e => e.Group)
            .Where(group => group.MaxBy(e => e.Id).End == GroupEnd.None)
            .Select(group => group.Key)
            .ToList();

    /// <summary>
    /// What a reversal may change is what it covers. An event that ends a group, or is in a group with a cancel,
    /// may change the group up to it, since how the group ends decides whether it's canceled.
    /// Any other event changes only itself.
    /// </summary>
    public IEnumerable<TimelineEvent> Changes(Timeline timeline, TimelineEvent e)
    {
        if (e.Undo != null)
            return CoveredBy(timeline, e);
        if (e.Group == SnowportId.Empty)
            return [e];
        var upToIt = Members(timeline, e.Group).Where(m => !Newer(m, e)).ToList();
        return e.End != GroupEnd.None || upToIt.Any(m => m.End == GroupEnd.Canceled) ? upToIt : [e];
    }

    public IEnumerable<TimelineEvent> InEffect(Timeline timeline) =>
        NewestFirst(timeline).Where(e => InEffect(timeline, e));

    #endregion
}

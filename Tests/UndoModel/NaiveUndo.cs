using System;
using System.Collections.Generic;
using System.Linq;
using Log = System.Collections.Generic.OrderedDictionary<SnowportId, TableEvent>;

/// <summary>
/// The undo rules written as plainly as possible, as the model the real implementations are checked against.
/// Every answer comes from scanning the whole log, so it's slow, but each rule reads as its definition.
/// </summary>
public sealed class NaiveUndo : IUndoApi
{
    public string Name => "naive";

    #region The rules

    private static bool Newer(TableEvent a, TableEvent b) => a.Id.CompareTo(b.Id) > 0;

    private static IEnumerable<TableEvent> NewestFirst(Log log) => log.Values.Reverse();

    /// <summary>
    /// The events of a unit. A unit is named by its first event: a group's first event names itself
    /// as the group, and an ungrouped event is a unit of one. Any other id names nothing.
    /// </summary>
    private static List<TableEvent> Members(Log log, SnowportId unit)
    {
        var first = log.Values.FirstOrDefault(e => e.Id == unit);
        if (first == null || first.Unit != unit)
            return [];
        if (first.Group == SnowportId.Empty)
            return [first];
        return log.Values.Where(e => e.Group == unit).ToList();
    }

    /// <summary>An event's history entry: an action's unit, or a reversal's own id.</summary>
    private static SnowportId Entry(TableEvent e) => e.Undo == null ? e.Unit : e.Id;

    /// <summary>The reversal with this id, if the entry is one. A reversal only reverses something older than itself.</summary>
    private static TableEvent ReversalAt(Log log, SnowportId entry) =>
        log.Values.FirstOrDefault(e =>
            e.Id == entry && e.Undo != null && e.Undo.Reverses.CompareTo(e.Id) < 0
        );

    /// <summary>Every entry a reversal reverses: its own, and the other reversals it retires with it.</summary>
    private static IEnumerable<SnowportId> Reversed(UndoFlag f) =>
        (f.Also ?? []).Prepend(f.Reverses);

    /// <summary>
    /// An event's history entry is in effect unless a newer reversal, itself in effect, reverses it.
    /// </summary>
    private static bool InEffect(Log log, TableEvent e) =>
        !log.Values.Any(reversal =>
            reversal.Undo != null
            && Reversed(reversal.Undo).Contains(Entry(e))
            && Newer(reversal, e)
            && InEffect(log, reversal)
        );

    /// <summary>
    /// History is the only source of truth: an event is undone when its entry isn't in effect.
    /// The same as the interface's, asked directly.
    /// </summary>
    public bool IsUndone(Log log, TableEvent e) => !InEffect(log, e);

    /// <summary>The unit an entry comes down to: an action's own, or for a reversal, that of what it reverses.</summary>
    public static SnowportId UnitOf(Log log, SnowportId entry) =>
        ReversalAt(log, entry) is { } reversal ? UnitOf(log, reversal.Undo.Reverses) : entry;

    /// <summary>
    /// Who an entry counts as done by, for the player <paramref name="me"/>: an action by whoever started it,
    /// another player's reversal by them, and the player's own reversal by whoever did what it reverses.
    /// </summary>
    private static byte AuthorOf(Log log, SnowportId entry, byte me) =>
        ReversalAt(log, entry) is not { } reversal ? entry.source
        : reversal.Id.source == me ? AuthorOf(log, reversal.Undo.Reverses, me)
        : reversal.Id.source;

    /// <summary>
    /// A unit is finished when it's a single ungrouped event, or when its newest event closes it.
    /// Until then it's a gesture in progress.
    /// </summary>
    public static bool IsClosed(Log log, SnowportId unit)
    {
        var members = Members(log, unit);
        if (members.Count == 0)
            return false;
        if (members[0].Group == SnowportId.Empty)
            return true;
        return members.MaxBy(e => e.Id).Close;
    }

    /// <summary>Whether any record the unit's events write is in scope, as done by the author.</summary>
    private static bool InScope(
        Log log,
        SnowportId unit,
        byte author,
        Func<byte, Replicated, bool> scope
    ) => Members(log, unit).Any(m => m.Records.Any(r => scope(author, r)));

    /// <summary>
    /// Whether Undo or Redo may act on the event: it isn't an admin event, the unit its entry
    /// comes down to is finished, and in scope as done by the entry's author.
    /// </summary>
    private static bool IsCandidate(
        Log log,
        TableEvent e,
        byte me,
        Func<byte, Replicated, bool> scope
    )
    {
        if (e.IsAdmin)
            return false;
        var unit = UnitOf(log, Entry(e));
        return IsClosed(log, unit) && InScope(log, unit, AuthorOf(log, Entry(e), me), scope);
    }

    /// <summary>
    /// The events a reversal may change: those of the unit its entry comes down to, older than the reversal.
    /// </summary>
    private static IEnumerable<TableEvent> CoveredBy(Log log, TableEvent reversal) =>
        Members(log, UnitOf(log, reversal.Id)).Where(m => Newer(reversal, m));

    #endregion

    #region Undo and redo

    /// <summary>
    /// Undo walks back through history. The player's latest undo and redo reversals are passed over,
    /// so each reversal goes one step further back. Past them everything is history, the player's own
    /// older undo reversals included, and the newest entry still in effect is reversed.
    /// A Redo reversal is never history: it cancels out with the Undo reversal it reversed.
    /// </summary>
    public UndoFlag Undo(Log log, byte me, Func<byte, Replicated, bool> scope)
    {
        bool pastLatestReversals = false;
        foreach (var e in NewestFirst(log))
        {
            if (e.Undo is { ByRedo: true } || !IsCandidate(log, e, me, scope))
                continue;
            if (e.Undo != null && e.Id.source == me && !pastLatestReversals)
                continue;
            pastLatestReversals = true;
            if (InEffect(log, e))
                return Reverse(log, e);
        }
        return null;
    }

    /// <summary>
    /// The reversal that reverses an event's history entry. When that entry is itself a reversal, the other
    /// reversals still in effect on the entry it reversed are retired too, so that comes back whoever else
    /// reversed it as well.
    /// </summary>
    private static UndoFlag Reverse(Log log, TableEvent e, bool byRedo = false)
    {
        var entry = Entry(e);
        var also = ReversalAt(log, entry) is { } target
            ? log
                .Values.Where(other =>
                    other.Id != entry
                    && ReversalAt(log, other.Id) != null
                    && Reversed(other.Undo).Contains(target.Undo.Reverses)
                    && InEffect(log, other)
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
    public UndoFlag Redo(Log log, byte me, Func<byte, Replicated, bool> scope)
    {
        foreach (var e in NewestFirst(log))
        {
            if (!IsCandidate(log, e, me, scope))
                continue;
            if (e.Undo == null || e.Id.source != me)
                return null;
            if (!e.Undo.ByRedo && InEffect(log, e))
                return Reverse(log, e, byRedo: true);
        }
        return null;
    }

    #endregion

    #region Groups and changes

    /// <summary>The groups a player started whose newest event doesn't close them.</summary>
    public List<SnowportId> OpenGroups(Log log, byte source) =>
        log
            .Values.Where(e => e.Group != SnowportId.Empty && e.Group.source == source)
            .GroupBy(e => e.Group)
            .Where(group => !group.MaxBy(e => e.Id).Close)
            .Select(group => group.Key)
            .ToList();

    /// <summary>What a reversal may change is what it covers; any other event changes only itself.</summary>
    public IEnumerable<TableEvent> Changes(Log log, TableEvent e) =>
        e.Undo == null ? [e] : CoveredBy(log, e);

    public IEnumerable<TableEvent> InEffect(Log log) =>
        NewestFirst(log).Where(e => InEffect(log, e));

    #endregion
}

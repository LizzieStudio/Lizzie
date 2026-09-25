using System.Collections.Generic;

/// <summary>
/// Utility functions on the event log for undo and redo.
/// </summary>
/// <remarks>
/// Whether an event is undone depends only on the undo events after it,
/// so a scan from newest to oldest can track the undone set as it goes and stop early.
/// </remarks>
public static class UndoLog
{
    /// <summary>
    /// True when the events are something the user can undo, which is when any has an effect.
    /// </summary>
    private static bool IsUndoable(IEnumerable<TableEvent> events)
    {
        foreach (var e in events)
            if (e.Effects.Length > 0)
                return true;
        return false;
    }

    /// <summary>
    /// True when the event is undone.
    /// </summary>
    public static bool IsUndone(TableEvent e, HashSet<SnowportId> undone) =>
        undone.Contains(e.Unit);

    /// <summary>
    /// Call on each event while walking the log from newest to oldest.
    /// False when the event is undone. Records the target of each live undo.
    /// </summary>
    public static bool Visit(TableEvent e, HashSet<SnowportId> undone)
    {
        if (IsUndone(e, undone))
            return false;
        if (e.Action is UndoAction u)
            undone.Add(u.Target);
        return true;
    }

    /// <summary>
    /// Follows an event's undo target chain down to the event it ultimately reverses.
    /// </summary>
    private static TableEvent ResolveBase(
        TableEvent e,
        OrderedDictionary<SnowportId, TableEvent> log
    )
    {
        while (e is { Action: UndoAction u })
            log.TryGetValue(u.Target, out e);
        return e;
    }

    /// <summary>
    /// The events an undo of <paramref name="targetId"/> reverses.
    /// </summary>
    private static List<TableEvent> ResolveEvents(
        OrderedDictionary<SnowportId, TableEvent> log,
        SnowportId targetId
    )
    {
        var events = new List<TableEvent>();
        if (!log.TryGetValue(targetId, out var e))
            return events;

        var @base = ResolveBase(e, log);
        if (@base == null)
            return events;

        var unit = @base.Unit;
        if (!log.TryGetValue(unit, out var first))
            return events;

        // an ungrouped event is its own unit
        if (first.Group == SnowportId.Empty)
        {
            events.Add(first);
            return events;
        }

        // A group's events all come after the event that names it.
        for (int i = log.IndexOf(unit); i < log.Count; i++)
        {
            var member = log.GetAt(i).Value;
            if (member.Unit != unit)
                continue;
            events.Add(member);
            if (member.Close)
                break;
        }

        return events;
    }

    /// <summary>
    /// The undo target for the given source.
    /// The event that should be undone by the Undo action.
    /// </summary>
    public static SnowportId? ComputeUndoTarget(
        OrderedDictionary<SnowportId, TableEvent> log,
        byte source
    )
    {
        var undone = new HashSet<SnowportId>();
        bool sawNonUndo = false;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;

            if (e.Id.source != source)
                continue;

            if (!sawNonUndo)
            {
                if (e.Action is UndoAction u)
                {
                    if (!IsUndone(e, undone))
                        undone.Add(u.Target);
                    continue;
                }
                sawNonUndo = true;
            }

            if (IsUndone(e, undone))
                continue;

            if (IsUndoable(ResolveEvents(log, e.Unit)))
                return e.Unit;
        }

        return null;
    }

    /// <summary>
    /// The redo target for the given source.
    /// The event that should be redone by the Redo action.
    /// </summary>
    public static SnowportId? ComputeRedoTarget(
        OrderedDictionary<SnowportId, TableEvent> log,
        byte source
    )
    {
        var undone = new HashSet<SnowportId>();

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            bool live = Visit(e, undone);

            if (e.Id.source != source)
                continue;

            // any regular action prevents the Redo action
            if (e.Action is not UndoAction u)
                return null;

            if (live && !u.Redo)
                return e.Id;
        }

        return null;
    }

    /// <summary>
    /// The records of type <typeparamref name="T"/> whose value an undo of <paramref name="targetId"/> should change.
    /// </summary>
    public static HashSet<SnowTag> ResolveAffected<T>(
        OrderedDictionary<SnowportId, TableEvent> log,
        SnowportId targetId
    )
        where T : class, IReplicated
    {
        var affected = new HashSet<SnowTag>();
        foreach (var e in ResolveEvents(log, targetId))
        foreach (var fx in e.Effects)
            if (fx is UpdateReplicatedEffect<T>)
                affected.Add(fx.Id);

        return affected;
    }

    /// <summary>
    /// The newest non-undone record for each of <paramref name="ids"/>, or null where none remains.
    /// </summary>
    public static Dictionary<SnowTag, T> LatestReplicated<T>(
        OrderedDictionary<SnowportId, TableEvent> log,
        IReadOnlyCollection<SnowTag> ids
    )
        where T : class, IReplicated
    {
        var winners = new Dictionary<SnowTag, T>(ids.Count);
        var pending = new HashSet<SnowTag>(ids);
        var undone = new HashSet<SnowportId>();

        for (int i = log.Count - 1; i >= 0 && pending.Count > 0; i--)
        {
            var e = log.GetAt(i).Value;
            if (!Visit(e, undone))
                continue;

            foreach (var fx in e.Effects)
                if (fx is UpdateReplicatedEffect<T> u && pending.Remove(u.Id))
                    winners[u.Id] = (T)u.Payload?.WithIdentity(u.Id, e.Id);
        }

        foreach (var id in pending)
            winners[id] = null;

        return winners;
    }

    /// <summary>
    /// True when an undo of <paramref name="targetId"/> should change the value of type <typeparamref name="T"/>.
    /// </summary>
    public static bool ResolveAffectsValue<T>(
        OrderedDictionary<SnowportId, TableEvent> log,
        SnowportId targetId
    )
    {
        foreach (var e in ResolveEvents(log, targetId))
        foreach (var fx in e.Effects)
            if (fx is SetReplicatedValueEffect<T>)
                return true;
        return false;
    }

    /// <summary>
    /// The newest non-undone value of type <typeparamref name="T"/> and the id of the event that wrote it.
    /// False if none remains.
    /// </summary>
    public static bool LatestValue<T>(
        OrderedDictionary<SnowportId, TableEvent> log,
        out T value,
        out SnowportId writeId
    )
    {
        var undone = new HashSet<SnowportId>();

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            if (!Visit(e, undone))
                continue;

            for (int j = e.Effects.Length - 1; j >= 0; j--)
                if (e.Effects[j] is SetReplicatedValueEffect<T> fx && fx.Payload is not null)
                {
                    value = fx.Payload;
                    writeId = e.Id;
                    return true;
                }
        }

        value = default;
        writeId = SnowportId.Empty;
        return false;
    }
}

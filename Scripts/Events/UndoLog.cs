using System.Collections.Generic;

/// <summary>
/// Utility functions on the event log for undo and redo.
/// </summary>
public static class UndoLog
{
    /// <summary>True when an event affects at least one component, or clears the table.</summary>
    public static bool IsComponentEvent(TableEvent e)
    {
        foreach (var fx in e.Effects)
            if (fx is ComponentEffect or TableClearEffect)
                return true;
        return false;
    }

    /// <summary>
    /// True when an event is something the user can undo:
    /// * component change
    /// * table clear
    /// * datarow upsert
    /// </summary>
    public static bool IsUndoableEvent(TableEvent e)
    {
        foreach (var fx in e.Effects)
            if (fx is ComponentEffect or TableClearEffect or UpdateReplicatedEffect<DataRow>)
                return true;
        return false;
    }

    /// <summary>True when an event carries a <see cref="TableClearEffect"/>.</summary>
    public static bool HasTableClear(TableEvent e)
    {
        foreach (var fx in e.Effects)
            if (fx is TableClearEffect)
                return true;
        return false;
    }

    /// <summary>
    /// True when an event has at least one drag or drop.
    /// </summary>
    public static bool IsDragEvent(TableEvent e)
    {
        foreach (var fx in e.Effects)
            if (
                fx is ComponentEffect c
                && c.State?.Location == VisualComponentBase.ComponentLocation.Cursor
            )
                return true;
        return false;
    }

    private static Dictionary<SnowportId, TableEvent> Index(IReadOnlyList<TableEvent> log)
    {
        var byId = new Dictionary<SnowportId, TableEvent>(log.Count);
        foreach (var e in log)
            byId[e.Id] = e;
        return byId;
    }

    /// <summary>
    /// Follows an event's undo target chain down to the event it ultimately reverses.
    /// </summary>
    private static TableEvent ResolveBase(TableEvent e, Dictionary<SnowportId, TableEvent> byId)
    {
        while (e is { Action: UndoAction u })
            byId.TryGetValue(u.Target, out e);
        return e;
    }

    /// <summary>
    /// The set of event ids that are currently undone.
    /// </summary>
    public static HashSet<SnowportId> ComputeUndone(IReadOnlyList<TableEvent> log)
    {
        var undone = new HashSet<SnowportId>();
        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log[i];
            bool active = !undone.Contains(e.Id);
            if (active && e.Action is UndoAction u)
                undone.Add(u.Target);
        }
        return undone;
    }

    /// <summary>
    /// The undo target for the given source.
    /// The event that should be undone by the Undo action.
    /// </summary>
    public static SnowportId? ComputeUndoTarget(IReadOnlyList<TableEvent> log, byte source)
    {
        var byId = Index(log);
        var undone = new HashSet<SnowportId>();
        bool sawNonUndo = false;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log[i];

            if (e.Id.source != source)
                continue;

            if (!sawNonUndo)
            {
                if (e.Action is UndoAction u)
                {
                    if (!undone.Contains(e.Id))
                        undone.Add(u.Target);
                    continue;
                }
                sawNonUndo = true;
            }

            if (undone.Contains(e.Id))
            {
                undone.Remove(e.Id);
                continue;
            }

            // drag events are ignored
            // only the drop event is undone
            if (IsDragEvent(e))
                continue;

            var @base = ResolveBase(e, byId);
            if (@base != null && IsUndoableEvent(@base))
                return e.Id;
        }

        return null;
    }

    /// <summary>
    /// The redo target for the given source.
    /// The event that should be redone by the Redo action.
    /// </summary>
    public static SnowportId? ComputeRedoTarget(IReadOnlyList<TableEvent> log, byte source)
    {
        var undone = ComputeUndone(log);

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log[i];

            if (e.Id.source != source)
                continue;

            // any regular action prevents the Redo action
            if (e.Action is not UndoAction u)
                return null;

            if (!undone.Contains(e.Id) && !u.Redo)
                return e.Id;
        }

        return null;
    }

    /// <summary>
    /// The components whose state an undo of <paramref name="targetId"/> should change.
    /// </summary>
    public static HashSet<SnowTag> ResolveAffectedComponents(
        IReadOnlyList<TableEvent> log,
        SnowportId targetId
    )
    {
        var affected = new HashSet<SnowTag>();
        var byId = Index(log);
        if (!byId.TryGetValue(targetId, out var e))
            return affected;

        var @base = ResolveBase(e, byId);
        if (@base == null)
            return affected;

        foreach (var fx in @base.Effects)
            if (fx is ComponentEffect)
                affected.Add(fx.Id);

        // A table clear "deletes" components without listing them,
        // so reconstructing across it must revisit every component.
        if (HasTableClear(@base))
            foreach (var ev in log)
            foreach (var fx in ev.Effects)
                if (fx is ComponentEffect)
                    affected.Add(fx.Id);

        return affected;
    }

    /// <summary>
    /// The dataset rows whose value an undo of <paramref name="targetId"/> should change.
    /// </summary>
    public static HashSet<SnowTag> ResolveAffectedRows(
        IReadOnlyList<TableEvent> log,
        SnowportId targetId
    )
    {
        var affected = new HashSet<SnowTag>();
        var byId = Index(log);
        if (!byId.TryGetValue(targetId, out var e))
            return affected;

        var @base = ResolveBase(e, byId);
        if (@base == null)
            return affected;

        foreach (var fx in @base.Effects)
            if (fx is UpdateReplicatedEffect<DataRow>)
                affected.Add(fx.Id);

        return affected;
    }

    /// <summary>
    /// The newest non-undone record for <paramref name="id"/>, or null if none remains.
    /// </summary>
    public static T LatestReplicated<T>(
        IReadOnlyList<TableEvent> log,
        SnowTag id,
        HashSet<SnowportId> undone
    )
        where T : class, IReplicated
    {
        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log[i];
            if (undone.Contains(e.Id))
                continue;

            foreach (var fx in e.Effects)
                if (fx is UpdateReplicatedEffect<T> u && u.Id == id)
                    return u.Payload;
        }
        return null;
    }
}

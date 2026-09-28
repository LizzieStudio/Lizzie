using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Whose actions an undo or redo walks through. Only the player's own undos walk back and forth;
/// anything another player does, undos included, is an action of theirs.
/// </summary>
public enum UndoStream
{
    /// <summary>The player's own actions, and their undos of them.</summary>
    Own,

    /// <summary>Other players' actions, and the player's undos of them.</summary>
    Others,

    /// <summary>Everyone's actions, and the player's undos of them.</summary>
    Anyone,
}

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
    /// Call on each event while walking the log from newest to oldest.
    /// False when the event is undone. Records the target of each live undo.
    /// </summary>
    public static bool VisitAndTrackUndone(TableEvent e, HashSet<SnowportId> undone)
    {
        if (undone.Contains(e.Unit))
            return false;
        if (e.Action is UndoAction u)
            undone.Add(u.Target);
        return true;
    }

    /// <summary>
    /// Follows an event's undo target chain down to the event it ultimately reverses.
    /// As in, if an Undo targets an Undo, where does it stop?
    /// </summary>
    private static TableEvent RealEventTarget(
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
    /// Either one event or the group if it belongs to one.
    /// </summary>
    private static List<TableEvent> AllTargetEvents(
        OrderedDictionary<SnowportId, TableEvent> log,
        SnowportId targetId
    )
    {
        if (!log.TryGetValue(targetId, out var e))
            return [];

        var baseEvent = RealEventTarget(e, log);
        if (baseEvent == null)
            return [];

        var unit = baseEvent.Unit;
        if (!log.TryGetValue(unit, out var first))
            return [];

        // an ungrouped event is its own unit
        if (first.Group == SnowportId.Empty)
            return [first];

        var events = new List<TableEvent>();

        // A group's events all come after the event that names it.
        for (int i = log.IndexOf(unit); i < log.Count; i++)
        {
            var member = log.GetAt(i).Value;
            if (member.Unit == unit)
            {
                events.Add(member);
                if (member.Close)
                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// Whether an event is in <paramref name="stream"/> for <paramref name="localSource"/>,
    /// and changes something in <paramref name="scope"/>.
    /// An undo is judged by what it reverses, and a group as a whole.
    /// </summary>
    private static bool InStream(
        OrderedDictionary<SnowportId, TableEvent> log,
        TableEvent e,
        byte localSource,
        Func<Effect, bool> scope,
        UndoStream stream,
        Dictionary<SnowportId, bool> scoped
    )
    {
        if (stream != UndoStream.Anyone)
        {
            TableEvent action = e;
            // follows the undo chain for as long as they were issued by the local player
            while (action is { Action: UndoAction u } && action.Id.source == localSource)
                log.TryGetValue(u.Target, out action);
            if (action == null)
                return false; // the undo chain didn't resolve to an event
            if (action.Id.source == localSource && stream != UndoStream.Own)
                return false;
            if (action.Id.source != localSource && stream == UndoStream.Own)
                return false;
        }

        var baseEvent = RealEventTarget(e, log);
        if (baseEvent == null)
            return false;

        // get the cached result for baseEvent
        var wasCached = scoped.TryGetValue(baseEvent.Unit, out bool inScope);

        if (!wasCached)
        {
            inScope = AllTargetEvents(log, baseEvent.Unit).Any(m => m.Effects.Any(scope));
            scoped[baseEvent.Unit] = inScope;
        }

        return inScope;
    }

    /// <summary>
    /// The event that Undo should reverse: the newest one in <paramref name="stream"/>
    /// that changes something in <paramref name="scope"/>.
    /// </summary>
    public static SnowportId? ComputeUndoTarget(
        OrderedDictionary<SnowportId, TableEvent> log,
        byte localSource,
        Func<Effect, bool> scope,
        UndoStream stream = UndoStream.Own
    )
    {
        var undone = new HashSet<SnowportId>();
        var scoped = new Dictionary<SnowportId, bool>();
        bool inUndoRedo = true;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;

            // check to see if this event hasn't been undone by anyone
            bool live = VisitAndTrackUndone(e, undone);

            // skip events outside this stream or scope
            if (!InStream(log, e, localSource, scope, stream, scoped))
                continue;

            if (inUndoRedo)
            {
                // if this is a local undo, continue to the next potential target (undo/redo chain)
                if (e.Action is UndoAction && e.Id.source == localSource)
                    continue;
                // once you see any event that isn't a local undo, stop checking (unfold the chain)
                inUndoRedo = false;
            }

            // if the event has already been undone by anyone, continue to the next potential target
            if (!live)
                continue;

            if (IsUndoable(AllTargetEvents(log, e.Unit)))
                return e.Unit;
        }

        return null;
    }

    /// <summary>
    /// The undo that Redo should reverse, in the same stream as <see cref="ComputeUndoTarget"/>.
    /// A newer event in the stream that isn't an undo means there's nothing to redo.
    /// </summary>
    public static SnowportId? ComputeRedoTarget(
        OrderedDictionary<SnowportId, TableEvent> log,
        byte localSource,
        Func<Effect, bool> scope,
        UndoStream stream = UndoStream.Own
    )
    {
        var undone = new HashSet<SnowportId>();
        var scoped = new Dictionary<SnowportId, bool>();

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            bool live = VisitAndTrackUndone(e, undone);

            if (InStream(log, e, localSource, scope, stream, scoped))
            {
                // A newer action, including another player's undo, prevents the Redo action.
                if (e.Action is not UndoAction || e.Id.source != localSource)
                    return null;
                var u = (UndoAction)e.Action;

                if (live && !u.Redo)
                    return e.Id;
            }
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
        foreach (var e in AllTargetEvents(log, targetId))
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
            if (!VisitAndTrackUndone(e, undone))
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
        foreach (var e in AllTargetEvents(log, targetId))
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
            if (!VisitAndTrackUndone(e, undone))
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

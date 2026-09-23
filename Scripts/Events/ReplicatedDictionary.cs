using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Storage for <see cref="IReplicated"/> objects that syncs during multiplayer transactionally.
/// </summary>
public sealed class ReplicatedDictionary<TEntity> : IReplicatedContainer
    where TEntity : class, IReplicated
{
    private EventSynchronizer _synchronizer;

    /// <summary>Starts merging events from the synchronizer into this store.</summary>
    public void Attach(EventSynchronizer synchronizer)
    {
        if (synchronizer == null || ReferenceEquals(_synchronizer, synchronizer))
            return;

        Detach();
        _synchronizer = synchronizer;
        _synchronizer.Applied += OnEventApplied;
    }

    /// <summary>Stops merging events. Safe to call when not attached.</summary>
    public void Detach()
    {
        if (_synchronizer == null)
            return;

        _synchronizer.Applied -= OnEventApplied;
        _synchronizer = null;
    }

    public Type RecordType => typeof(TEntity);

    private Dictionary<SnowTag, TEntity> dict = new();

    // changes merged while the synchronizer is bulk loading, sent by FlushBulkLoad
    private Dictionary<SnowTag, (TEntity Old, TEntity New)> pending = new();

    public IReadOnlyDictionary<SnowTag, TEntity> Records
    {
        get { return dict; }
    }

    #region events

    private HashSet<Action<IReadOnlyDictionary<SnowTag, TEntity>>> observers = new();
    private Dictionary<SnowTag, Action<TEntity>> observersOfId = new();

    public event Action<IReadOnlyList<RecordChange>> Changed;

    /// <summary>
    /// Immediately calls <paramref name="callback"/> with the full dictionary of records,
    /// then calls <paramref name="callback"/> with updated records whenever they change.
    /// </summary>
    /// <param name="callback">The action to call.</param>
    public void Observe(Action<IReadOnlyDictionary<SnowTag, TEntity>> callback)
    {
        observers.Add(callback);
        callback(Records);
    }

    /// <summary>
    /// Immediately calls <paramref name="callback"/> with the record,
    /// then calls <paramref name="callback"/> with the updated record whenever it changes.
    /// </summary>
    /// <param name="callback">The action to call.</param>
    public void Observe(SnowTag Id, Action<TEntity> callback)
    {
        observersOfId[Id] = observersOfId.GetValueOrDefault(Id) + callback;
        if (dict.TryGetValue(Id, out var value))
        {
            callback(value);
        }
    }

    /// <summary>
    /// Stops calling <paramref name="callback"/> with mass updates.
    /// </summary>
    /// <param name="callback">The action to stop calling.</param>
    public void Unobserve(Action<IReadOnlyDictionary<SnowTag, TEntity>> callback)
    {
        observers.Remove(callback);
    }

    /// <summary>
    /// Stops calling <paramref name="callback"/> with updates.
    /// </summary>
    /// <param name="callback">The action to stop calling.</param>
    public void Unobserve(SnowTag Id, Action<TEntity> callback)
    {
        observersOfId[Id] = observersOfId.GetValueOrDefault(Id) - callback;
    }

    private void NotifyChanged(IReadOnlyDictionary<SnowTag, (TEntity Old, TEntity New)> changed)
    {
        // copies in case a callback unobserves
        foreach (var callback in observers.ToArray())
        {
            callback(dict);
        }

        foreach (var (id, change) in changed)
        {
            observersOfId.GetValueOrDefault(id)?.Invoke(change.New);
        }

        Changed?.Invoke(changed.Values.Select(c => new RecordChange(c.Old, c.New)).ToArray());
    }

    /// <summary>
    /// Removes every record, e.g. when the project is replaced.
    /// Observers receive either an empty dictionary or null.
    /// </summary>
    public void Clear()
    {
        var removed = dict.Values.ToArray();
        dict.Clear();
        pending.Clear();

        foreach (var callback in observers.ToArray())
        {
            callback(dict);
        }

        foreach (var entity in removed)
        {
            observersOfId.GetValueOrDefault(entity.Id)?.Invoke(null);
        }

        Changed?.Invoke(removed.Select(r => new RecordChange(r, null)).ToArray());
    }

    #endregion

    /// <summary>Recomputes every record the undone event touched from the rest of the log.</summary>
    private void OnUndo(UndoAction undo)
    {
        var log = _synchronizer?.EventLog;
        if (log == null)
            return;

        var undone = UndoLog.ComputeUndone(log);
        var changed = new Dictionary<SnowTag, (TEntity Old, TEntity New)>();

        foreach (var id in UndoLog.ResolveAffected<TEntity>(log, undo.Target))
        {
            var old = dict.GetValueOrDefault(id);
            var winner = UndoLog.LatestReplicated<TEntity>(log, id, undone);
            if (winner == null)
                dict.Remove(id);
            else
                dict[id] = winner;
            changed[id] = (old, winner);
        }

        Publish(changed);
    }

    /// <summary>Merges every replicated effect in the event into the store.</summary>
    private void OnEventApplied(TableEvent e)
    {
        if (dict == null)
            return;

        if (e.Action is UndoAction undo)
        {
            OnUndo(undo);
            return;
        }

        var changed = new Dictionary<SnowTag, (TEntity Old, TEntity New)>();

        foreach (var fx in e.Effects.OfType<UpdateReplicatedEffect<TEntity>>())
        {
            var entity = fx.Payload;
            if (entity == null)
                continue;

            if (
                dict.TryGetValue(fx.Id, out var current)
                && e.Id.CompareTo(current.LastUpdateId) < 0
            )
                continue;

            // an earlier effect in this event may have already replaced it
            var old = changed.TryGetValue(fx.Id, out var prior) ? prior.Old : current;
            dict[fx.Id] = entity;
            changed[fx.Id] = (old, entity);
        }

        Publish(changed);
    }

    private void Publish(Dictionary<SnowTag, (TEntity Old, TEntity New)> changed)
    {
        if (changed.Count == 0)
            return;

        if (_synchronizer?.BulkLoading == true)
        {
            // keeps the value from before the bulk load began
            foreach (var (id, change) in changed)
                pending[id] = pending.TryGetValue(id, out var first)
                    ? (first.Old, change.New)
                    : change;
            return;
        }

        NotifyChanged(changed);
    }

    /// <summary>
    /// Sends one notification for everything merged during a bulk load.
    /// Call once the synchronizer stops bulk loading.
    /// </summary>
    public void FlushBulkLoad()
    {
        if (pending.Count == 0)
            return;

        var changed = pending;
        pending = new();
        NotifyChanged(changed);
    }

    /// <summary>One upsert effect carrying the current value of every record.</summary>
    public IEnumerable<Effect> EnumerateSaveEffects() =>
        dict.Values.Select(r =>
            (Effect)new UpdateReplicatedEffect<TEntity> { Id = r.Id, Payload = r }
        );
}

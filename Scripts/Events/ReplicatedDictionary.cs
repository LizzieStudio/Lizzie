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

    public event Action<IReadOnlyList<RecordChange>> Changed;

    private void NotifyChanged(IReadOnlyDictionary<SnowTag, (TEntity Old, TEntity New)> changed)
    {
        Changed?.Invoke(changed.Values.Select(c => new RecordChange(c.Old, c.New)).ToArray());
    }

    /// <summary>
    /// Removes every record, e.g. when the project is replaced.
    /// Every removed record is reported as a change to null.
    /// </summary>
    public void Clear()
    {
        var removed = dict.Values.ToArray();
        dict.Clear();
        pending.Clear();

        Changed?.Invoke(removed.Select(r => new RecordChange(r, null)).ToArray());
    }

    #endregion

    private void OnEventApplied(TableEvent e)
    {
        var log = _synchronizer.EventLog;
        var ids = UndoLog
            .Changes(log, e)
            .SelectMany(c => c.Effects.OfType<UpdateReplicatedEffect<TEntity>>())
            .Select(fx => fx.Id)
            .ToHashSet();
        if (ids.Count == 0)
            return;

        var latest = ids.ToDictionary(id => id, _ => (TEntity)null);
        var unfound = new HashSet<SnowTag>(ids);
        foreach (var writer in UndoLog.InEffect(log))
        {
            for (int j = writer.Effects.Length - 1; j >= 0; j--)
                if (
                    writer.Effects[j] is UpdateReplicatedEffect<TEntity> { Payload: { } payload } fx
                    && unfound.Remove(fx.Id)
                )
                    latest[fx.Id] = (TEntity)payload.WithIdentity(fx.Id, writer.Id);
            if (unfound.Count == 0)
                break;
        }

        var changed = new Dictionary<SnowTag, (TEntity Old, TEntity New)>();
        foreach (var (id, now) in latest)
        {
            var old = dict.GetValueOrDefault(id);
            if (Equals(old, now))
                continue;
            if (now == null)
                dict.Remove(id);
            else
                dict[id] = now;
            changed[id] = (old, now);
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

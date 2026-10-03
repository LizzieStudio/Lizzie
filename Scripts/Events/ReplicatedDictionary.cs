using System;
using System.Collections.Generic;
using System.Linq;
using Log = System.Collections.Generic.OrderedDictionary<SnowportId, TableEvent>;

/// <summary>
/// Storage for <see cref="IReplicated"/> objects that syncs during multiplayer transactionally.
/// </summary>
public sealed class ReplicatedDictionary<TEntity> : IReplicatedStore
    where TEntity : class, IReplicated
{
    private static readonly bool Saved = !typeof(TEntity).IsDefined(
        typeof(NotSavedAttribute),
        true
    );

    public Type RecordType => typeof(TEntity);

    private Dictionary<SnowTag, TEntity> dict = new();

    // changes merged while the synchronizer is bulk loading, sent by FlushBulkLoad
    private Dictionary<SnowTag, (TEntity Old, TEntity New)> pending = new();

    public IReadOnlyDictionary<SnowTag, TEntity> Records
    {
        get { return dict; }
    }

    public IReplicated Find(SnowTag id) => dict.GetValueOrDefault(id);

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

    public void Apply(Log log, IReadOnlyList<TableEvent> changed, bool bulkLoading)
    {
        var ids = changed
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

        var merged = new Dictionary<SnowTag, (TEntity Old, TEntity New)>();
        foreach (var (id, now) in latest)
        {
            var old = dict.GetValueOrDefault(id);
            if (Equals(old, now))
                continue;
            if (now == null)
                dict.Remove(id);
            else
                dict[id] = now;
            merged[id] = (old, now);
        }

        Publish(merged, bulkLoading);
    }

    private void Publish(Dictionary<SnowTag, (TEntity Old, TEntity New)> changed, bool bulkLoading)
    {
        if (changed.Count == 0)
            return;

        if (bulkLoading)
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

    /// <summary>
    /// One upsert effect carrying the current value of every record, unless the type is <see cref="NotSavedAttribute"/>.
    /// </summary>
    public IEnumerable<Effect> EnumerateSaveEffects() =>
        Saved
            ? dict.Values.Select(r =>
                (Effect)new UpdateReplicatedEffect<TEntity> { Id = r.Id, Payload = r }
            )
            : [];
}

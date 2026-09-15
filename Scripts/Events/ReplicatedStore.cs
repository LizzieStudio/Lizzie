using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Storage for <see cref="IReplicated"/> objects that syncs during multiplayer transactionally.
/// </summary>
public abstract partial class ReplicatedStore<TEntity> : Node
    where TEntity : class, IReplicated
{
    protected abstract IDictionary<SnowTag, TEntity> Store { get; }

    protected abstract void NotifyChanged(IReadOnlyList<SnowTag> ids);

    /// <summary>Captures the non-deleted definitions for a joining client.</summary>
    public virtual Effect[] GenerateCatchupEffects()
    {
        var store = Store;
        if (store == null)
            return Array.Empty<Effect>();

        return store
            .Values.Where(e => !e.Deleted)
            .Select(e => (Effect)new UpdateReplicatedEffect<TEntity> { Id = e.Id, Payload = e })
            .ToArray();
    }

    /// <summary>Merges every replicated effect in the event into the store.</summary>
    protected void OnEventApplied(TableEvent e)
    {
        var store = Store;
        if (store == null)
            return;

        var changed = new List<SnowTag>();

        foreach (var fx in e.Effects.OfType<UpdateReplicatedEffect<TEntity>>())
        {
            var entity = fx.Payload;
            if (entity == null)
                continue;

            if (
                store.TryGetValue(fx.Id, out var current)
                && e.Id.CompareTo(current.LastUpdateId) < 0
            )
                continue;

            entity.Id = fx.Id;
            entity.LastUpdateId = e.Id;
            store[fx.Id] = entity;
            changed.Add(fx.Id);
        }

        if (changed.Count > 0)
            NotifyChanged(changed);
    }
}

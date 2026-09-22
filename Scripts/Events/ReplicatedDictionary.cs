using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Storage for <see cref="IReplicated"/> objects that syncs during multiplayer transactionally.
/// </summary>
public sealed class ReplicatedDictionary<TEntity>
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

    private Dictionary<SnowTag, TEntity> dict = new();

    public IReadOnlyDictionary<SnowTag, TEntity> Records
    {
        get { return dict; }
    }

    #region events

    private HashSet<Action<IReadOnlyDictionary<SnowTag, TEntity>>> observers = new();

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
    /// Stops calling <paramref name="callback"/> with updates.
    /// </summary>
    /// <param name="callback">The action to stop calling.</param>
    public void Unobserve(Action<IReadOnlyDictionary<SnowTag, TEntity>> callback)
    {
        observers.Remove(callback);
    }

    private void NotifyChanged(IReadOnlyDictionary<SnowTag, TEntity> ids)
    {
        foreach (var callback in observers)
        {
            callback(dict);
        }
    }

    #endregion

    private void OnUndo(UndoAction undo) { }

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

        var changed = new Dictionary<SnowTag, TEntity>();

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

            dict[fx.Id] = entity;
            changed.Add(fx.Id, entity);
        }

        if (changed.Count > 0)
            NotifyChanged(changed);
    }
}

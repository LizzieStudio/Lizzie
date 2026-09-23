using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Storage for a single <see cref="IReplicated"/> object that syncs during multiplayer transactionally.
/// </summary>
public sealed class ReplicatedValue<T> : IReplicatedContainer
{
    private EventSynchronizer _synchronizer;

    private readonly Func<T> _createDefault;

    private readonly Func<T, bool> _shouldPersist;

    public ReplicatedValue(Func<T> createDefault, Func<T, bool> shouldPersist = null)
    {
        _createDefault = createDefault;
        _shouldPersist = shouldPersist ?? (_ => true);
        _value = createDefault();
        _notified = _value;
    }

    /// <summary>Starts merging events from the synchronizer into this value.</summary>
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

    public Type RecordType => typeof(T);

    private T _value;

    // the value last reported by Changed
    private T _notified;

    // the id of the event that wrote _value
    private SnowportId _writeId = SnowportId.Empty;

    // a change merged while the synchronizer is bulk loading, sent by FlushBulkLoad
    private bool _pending;

    public T Value
    {
        get { return _value; }
    }

    #region events

    public event Action<IReadOnlyList<RecordChange>> Changed;

    private void NotifyChanged()
    {
        var old = _notified;
        _notified = _value;
        Changed?.Invoke([new RecordChange(old, _value)]);
    }

    /// <summary>
    /// Resets to the default value, e.g. when the project is replaced.
    /// </summary>
    public void Clear()
    {
        _value = _createDefault();
        _writeId = SnowportId.Empty;
        _pending = false;
        NotifyChanged();
    }

    #endregion

    /// <summary>Recomputes the value from the rest of the log if the undone event wrote it.</summary>
    private void OnUndo(UndoAction undo)
    {
        var log = _synchronizer?.EventLog;
        if (log == null)
            return;

        if (!UndoLog.ResolveAffectsValue<T>(log, undo.Target))
            return;

        var undone = UndoLog.ComputeUndone(log);
        if (UndoLog.LatestValue<T>(log, undone, out var value, out var writeId))
        {
            _value = value;
            _writeId = writeId;
        }
        else
        {
            _value = _createDefault();
            _writeId = SnowportId.Empty;
        }

        Publish();
    }

    /// <summary>Merges the last value effect in the event, if any.</summary>
    private void OnEventApplied(TableEvent e)
    {
        if (e.Action is UndoAction undo)
        {
            OnUndo(undo);
            return;
        }

        var fx = e.Effects.OfType<SetReplicatedValueEffect<T>>().LastOrDefault();
        if (fx == null || fx.Payload is null)
            return;

        if (e.Id.CompareTo(_writeId) < 0)
            return;

        _value = fx.Payload;
        _writeId = e.Id;
        Publish();
    }

    private void Publish()
    {
        if (_synchronizer?.BulkLoading == true)
        {
            _pending = true;
            return;
        }

        NotifyChanged();
    }

    /// <summary>
    /// Sends one notification for everything merged during a bulk load.
    /// Call once the synchronizer stops bulk loading.
    /// </summary>
    public void FlushBulkLoad()
    {
        if (!_pending)
            return;

        _pending = false;
        NotifyChanged();
    }

    /// <summary>The effect carrying the current value, unless it should not be persisted.</summary>
    public IEnumerable<Effect> EnumerateSaveEffects()
    {
        if (_value is null || !_shouldPersist(_value))
            yield break;
        yield return new SetReplicatedValueEffect<T> { Payload = _value };
    }
}

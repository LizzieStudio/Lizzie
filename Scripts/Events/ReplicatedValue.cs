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
        _pending = false;
        NotifyChanged();
    }

    #endregion

    private void OnEventApplied(TableEvent e)
    {
        var log = _synchronizer.EventLog;
        var changes = UndoLog.Changes(log, e).SelectMany(c => c.Effects);
        if (!changes.OfType<SetReplicatedValueEffect<T>>().Any())
            return;

        var latest = UndoLog
            .InEffect(log)
            .Select(writer =>
                writer
                    .Effects.OfType<SetReplicatedValueEffect<T>>()
                    .LastOrDefault(fx => fx.Payload is not null)
            )
            .FirstOrDefault(fx => fx != null);
        var value = latest == null ? _createDefault() : latest.Payload;
        if (EqualityComparer<T>.Default.Equals(value, _value))
            return;

        _value = value;
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

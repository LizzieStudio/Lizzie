using System;
using System.Collections.Generic;
using System.Linq;
using Log = System.Collections.Generic.OrderedDictionary<SnowportId, TableEvent>;

/// <summary>
/// Storage for a single value, like the project settings, that syncs during multiplayer transactionally.
/// The value isn't an <see cref="IReplicated"/>. It has no id of its own, and is read with <see cref="IRecordReader.Value{T}"/>.
/// Until an event sets it, it's <c>new T()</c>.
/// </summary>
public sealed class ReplicatedValue<T> : IReplicatedStore
    where T : class, new()
{
    public ReplicatedValue()
    {
        _value = new();
        _notified = _value;
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
        _value = new();
        _pending = false;
        NotifyChanged();
    }

    #endregion

    public void Apply(Log log, IReadOnlyList<TableEvent> changed, bool bulkLoading)
    {
        if (!changed.SelectMany(c => c.Effects).OfType<SetReplicatedValueEffect<T>>().Any())
            return;

        var latest = UndoLog
            .InEffect(log)
            .Select(writer =>
                writer
                    .Effects.OfType<SetReplicatedValueEffect<T>>()
                    .LastOrDefault(fx => fx.Payload is not null)
            )
            .FirstOrDefault(fx => fx != null);
        var value = latest == null ? new T() : latest.Payload;
        if (EqualityComparer<T>.Default.Equals(value, _value))
            return;

        _value = value;
        if (bulkLoading)
            _pending = true;
        else
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

    /// <summary>Always null, since it contains no records.</summary>
    public IReplicated Find(SnowTag id) => null;

    /// <summary>The effect carrying the current value, unless it's the default, which needs no saving.</summary>
    public IEnumerable<Effect> EnumerateSaveEffects()
    {
        if (_value is null || EqualityComparer<T>.Default.Equals(_value, new T()))
            yield break;
        yield return new SetReplicatedValueEffect<T> { Payload = _value };
    }
}

using System.Collections.Generic;
using Lizzie.Replication.Machinery;

/// <summary>
/// <para>A local value that a Watch can read like a record.</para>
///
/// A Watch that reads a <see cref="WatchableValue{0}"/> through its <see cref="IRecordReader"/> runs again at the end of the frame after the value changes.
/// It isn't synchronized during multiplayer or saved with the project. It starts unset.
/// </summary>
public sealed class WatchableValue<T>
{
    private T _value;
    private bool _set;

    // The watchers that read it since it last changed.
    private readonly HashSet<Watcher> _readers = [];

    public WatchableValue() { }

    public WatchableValue(T value)
    {
        _value = value;
        _set = true;
    }

    public void Set(T value)
    {
        if (!_set || !EqualityComparer<T>.Default.Equals(_value, value))
        {
            _value = value;
            _set = true;
            Changed();
        }
    }

    public void Unset()
    {
        if (_set)
        {
            _value = default;
            _set = false;
            Changed();
        }
    }

    /// <summary>
    /// Get the value. Returns false when unset.
    ///
    /// <para>Use <see cref="TryGet(IRecordReader R, out T value)"/> to subscribe to changes.</para>
    /// </summary>
    public bool TryGet(out T value)
    {
        value = _value;
        return _set;
    }

    /// <summary>
    /// Get the value. Returns false when unset.
    /// When <paramref name="R"/> is a Watch's reader, subscribes to changes.
    /// </summary>
    public bool TryGet(IRecordReader R, out T value)
    {
        if (R is Watcher watcher)
            _readers.Add(watcher);
        return TryGet(out value);
    }

    private void Changed()
    {
        foreach (var watcher in _readers)
            RecordService.Instance?.MarkDirty(watcher);
        // Readers will run again, adding them back if they read the value again.
        _readers.Clear();
    }
}

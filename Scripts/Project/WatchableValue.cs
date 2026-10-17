using System.Collections.Generic;
using Lizzie.Replication.Machinery;

/// <summary>
/// A local value that a Watch can read and react to like an atom.
///
/// <para>
/// A Watch that reads it with <see cref="Get"/> runs again at the end of the frame after the value changes.
/// It isn't synchronized during multiplayer or saved with the project. It starts as <c>default</c>.
/// </para>
/// </summary>
public sealed class WatchableValue<T>
{
    private T _value;

    // The watchers that read it since it last changed.
    private readonly HashSet<Watcher> _readers = [];

    /// <summary>
    /// The value, without subscribing to changes.
    /// Setting it runs the Watches that read it again.
    /// </summary>
    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            foreach (var watcher in _readers)
                AtomService.Instance?.Machinery().MarkDirty(watcher);
            // Readers will run again, adding them back if they read the value again.
            _readers.Clear();
        }
    }

    /// <summary>
    /// The value. When <paramref name="R"/> is a Watch's reader, subscribes to changes.
    /// </summary>
    public T Get(IAtomReader R)
    {
        if (R is Watcher watcher)
            _readers.Add(watcher);
        return _value;
    }
}

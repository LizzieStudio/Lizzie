using System;
using System.Collections.Generic;

/// <summary>
/// Holds the unique set of component References whose properties need to be synced.
/// A given Reference can only appear once — adding a duplicate is a no-op.
/// </summary>
public class ComponentPropertyQueue
{
    private readonly HashSet<Guid> _set = new();
    private readonly Queue<Guid> _queue = new();

    public int Count => _queue.Count;

    /// <summary>
    /// Queues a component Reference for sync. Silently ignores duplicates.
    /// </summary>
    public void Enqueue(Guid reference)
    {
        if (_set.Add(reference))
            _queue.Enqueue(reference);
    }

    /// <summary>
    /// Removes and returns the next Reference. Returns false when the queue is empty.
    /// </summary>
    public bool TryDequeue(out Guid reference)
    {
        if (_queue.TryDequeue(out reference))
        {
            _set.Remove(reference);
            return true;
        }
        return false;
    }
}

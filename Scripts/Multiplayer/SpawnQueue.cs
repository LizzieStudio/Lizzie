using System;
using System.Collections.Generic;

public class SpawnQueue
{
    private readonly HashSet<Guid> _set = new();
    private readonly Queue<Guid> _queue = new();

    public int Count => _queue.Count;

    /// <summary>
    /// Adds a reference to the queue. Does nothing if the reference is already queued.
    /// </summary>
    public void Enqueue(Guid reference)
    {
        if (_set.Add(reference))
            _queue.Enqueue(reference);
    }

    /// <summary>
    /// Removes and returns the next reference. Returns false if the queue is empty.
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

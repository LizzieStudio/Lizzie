using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// </summary>
public class EventLog
{
    private readonly HashSet<SnowportId> _applied = new(10000);

    // later we may need to make this a large array with a rolling index,
    // but that would require handling events that are too old.
    private readonly List<TableEvent> _ordered = new(10000);
    private readonly List<SnowportId> _keys = new(10000);

    public IReadOnlyList<TableEvent> Events => _ordered;

    /// <summary>
    /// The events newer than <paramref name="t"/>, in order.
    /// Used to stream the post-catchup backlog to a new player.
    /// </summary>
    public IEnumerable<TableEvent> EventsAfter(SnowportId t)
    {
        int idx = _keys.BinarySearch(t);
        idx = idx < 0 ? ~idx : idx + 1;
        return _ordered.GetRange(idx, _ordered.Count - idx);
    }

    public bool TryRecord(TableEvent e)
    {
        if (!_applied.Add(e.Id))
            return false;

        int idx = _keys.BinarySearch(e.Id);
        Debug.Assert(idx < 0);
        idx = ~idx;
        _keys.Insert(idx, e.Id);
        _ordered.Insert(idx, e);
        return true;
    }

    public void Clear()
    {
        _applied.Clear();
        _ordered.Clear();
        _keys.Clear();
    }
}

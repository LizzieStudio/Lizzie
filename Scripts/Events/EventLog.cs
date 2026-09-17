using System.Collections.Generic;

/// <summary>
/// Append-only log of table events in SnowportId order.
/// </summary>
public class EventLog
{
    private readonly HashSet<SnowportId> _applied = new(10000);

    private readonly List<TableEvent> _ordered = new(10000);

    public IReadOnlyList<TableEvent> Events => _ordered;

    public int Count => _ordered.Count;

    public bool TryRecord(TableEvent e)
    {
        if (!_applied.Add(e.Id))
            return false;

        // insert to maintain SnowportId order
        // this could be a BinarySearch, but most insertion happen at the end
        int i = _ordered.Count - 1;
        while (i >= 0 && _ordered[i].Id.CompareTo(e.Id) > 0)
            i--;
        _ordered.Insert(i + 1, e);
        return true;
    }

    public void Clear()
    {
        _applied.Clear();
        _ordered.Clear();
    }
}

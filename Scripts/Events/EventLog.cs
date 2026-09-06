using System.Collections.Generic;

/// <summary>
/// Append-only log of table events in arrival order.
/// </summary>
public class EventLog
{
    private readonly HashSet<SnowportId> _applied = new(10000);

    // later we may need to make this a large array with a rolling index,
    // but that would require handling events that are too old.
    private readonly List<TableEvent> _ordered = new(10000);

    public IReadOnlyList<TableEvent> Events => _ordered;

    public int Count => _ordered.Count;

    /// <summary>
    /// The events at or after <paramref name="index"/>, in order.
    /// Used to stream the post-catchup backlog to a new player.
    /// </summary>
    public IEnumerable<TableEvent> EventsFrom(int index) =>
        _ordered.GetRange(index, _ordered.Count - index);

    public bool TryRecord(TableEvent e)
    {
        if (!_applied.Add(e.Id))
            return false;

        _ordered.Add(e);
        return true;
    }

    public void Clear()
    {
        _applied.Clear();
        _ordered.Clear();
    }
}

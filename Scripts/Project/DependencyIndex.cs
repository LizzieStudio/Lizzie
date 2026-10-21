using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Tracks which watchers read which query values to update watchers when their dependencies change.
/// </summary>
public sealed class DependencyIndex<W>
    where W : class
{
    // per query, per value read, the watchers that read it
    private readonly Dictionary<Query, Dictionary<object, HashSet<W>>> _reads = new();

    // the same, for watchers that read only how many atoms a lookup key has
    private readonly Dictionary<Query, Dictionary<object, HashSet<W>>> _counts = new();

    // each watcher's entries, so they can be removed
    private readonly Dictionary<
        W,
        List<(Dictionary<object, HashSet<W>> Values, object Value)>
    > _held = new();

    /// <summary>
    /// Notes that <paramref name="watcher"/> read <paramref name="value"/> of <paramref name="query"/>,
    /// or, when <paramref name="count"/>, only how many atoms it has.
    /// </summary>
    public void Add(W watcher, Query query, object value, bool count = false)
    {
        var byQuery = count ? _counts : _reads;
        if (!byQuery.TryGetValue(query, out var values))
            byQuery[query] = values = new();
        if (!values.TryGetValue(value, out var watchers))
            values[value] = watchers = new();
        if (!watchers.Add(watcher))
            return;

        if (!_held.TryGetValue(watcher, out var held))
            _held[watcher] = held = new();
        held.Add((values, value));
    }

    /// <summary>
    /// Forgets everything <paramref name="watcher"/> read.
    /// </summary>
    public void Clear(W watcher)
    {
        if (!_held.Remove(watcher, out var held))
            return;
        foreach (var (values, value) in held)
        {
            var watchers = values[value];
            watchers.Remove(watcher);
            if (watchers.Count == 0)
                values.Remove(value);
        }
    }

    /// <summary>
    /// The watchers that read something <paramref name="touches"/> changed.
    /// </summary>
    public HashSet<W> Affected(IEnumerable<Touch> touches)
    {
        var affected = new HashSet<W>();
        foreach (var (query, value, count) in touches)
            if (
                (count ? _counts : _reads).TryGetValue(query, out var values)
                && values.TryGetValue(value, out var watchers)
            )
                affected.UnionWith(watchers);
        return affected;
    }
}

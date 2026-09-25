using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Untracked record reads, and watchers that rerun when the records they read change.
/// </summary>
public partial class ProjectService : IRecordReader
{
    private Dictionary<Type, IReplicatedContainer> _containersByType;

    private readonly List<Watcher> _watchers = new();

    // watchers to rerun at the end of the frame
    private readonly HashSet<Watcher> _dirty = new();

    /// <summary>
    /// Runs <paramref name="sync"/> at the end of the frame, then again whenever a record it
    /// read changes, until <paramref name="owner"/> leaves the tree. Call from _EnterTree.
    /// The first run is deferred so callers can configure the node after adding it.
    /// </summary>
    public void Watch(Node owner, Action<IRecordReader> sync)
    {
        var watcher = new Watcher(owner, sync, this);
        _watchers.Add(watcher);
        owner.Connect(
            Node.SignalName.TreeExiting,
            Callable.From(() => Unwatch(watcher)),
            (uint)ConnectFlags.OneShot
        );
        MarkDirty(watcher);
    }

    /// <summary>
    /// Schedules a rerun of the Node's Watch functions at the end of the frame.
    /// Does nothing if the Node is not in the tree.
    /// </summary>
    public void QueueSync(Node owner)
    {
        foreach (var watcher in _watchers.Where(w => w.Owner == owner))
        {
            MarkDirty(watcher);
        }
    }

    /// <summary>
    /// Runs the Node's Watch functions immediately.
    /// Useful if you need to measure the node as soon as it's added.
    /// </summary>
    public void SyncNow(Node owner)
    {
        foreach (var watcher in _watchers.Where(w => w.Owner == owner))
        {
            _dirty.Remove(watcher);
            watcher.Run();
        }
    }

    private void Unwatch(Watcher watcher)
    {
        _watchers.Remove(watcher);
        _dirty.Remove(watcher);
    }

    private void SubscribeWatchers()
    {
        _containersByType = Containers.ToDictionary(c => c.RecordType);
        foreach (var container in Containers)
        {
            var type = container.RecordType;
            container.Changed += changes => OnRecordsChanged(type, changes);
        }
    }

    private void OnRecordsChanged(Type type, IReadOnlyList<RecordChange> changes)
    {
        foreach (var watcher in _watchers)
        {
            if (!_dirty.Contains(watcher) && watcher.Affected(type, changes))
                MarkDirty(watcher);
        }
    }

    private void MarkDirty(Watcher watcher)
    {
        if (_dirty.Count == 0)
            Callable.From(FlushWatchers).CallDeferred();
        _dirty.Add(watcher);
    }

    /// <summary>
    /// Runs the dirty watchers, parents before their descendants, so a parent removes or
    /// replaces a child before the child can sync a record that no longer fits it.
    /// </summary>
    private void FlushWatchers()
    {
        var batch = _dirty.OrderBy(w => Depth(w.Owner)).ToArray();
        _dirty.Clear();

        foreach (var watcher in batch)
        {
            if (_watchers.Contains(watcher) && IsInstanceValid(watcher.Owner))
                watcher.Run();
        }
    }

    private static int Depth(Node node)
    {
        if (!IsInstanceValid(node))
            return 0;

        int depth = 0;
        for (var n = node.GetParent(); n != null; n = n.GetParent())
            depth++;
        return depth;
    }

    #region IRecordReader

    private IReplicatedContainer ContainerOf(Type type) =>
        _containersByType.TryGetValue(type, out var c)
            ? c
            : throw new InvalidOperationException($"No replicated container holds {type.Name}");

    private IReadOnlyDictionary<SnowTag, T> RecordsOf<T>()
        where T : class, IReplicated =>
        ContainerOf(typeof(T)) is ReplicatedDictionary<T> d
            ? d.Records
            : throw new InvalidOperationException($"{typeof(T).Name} is not in a dictionary");

    public T Get<T>(SnowTag id)
        where T : class, IReplicated =>
        RecordsOf<T>().TryGetValue(id, out var r) && !r.Deleted ? r : null;

    public T GetIncludingDeleted<T>(SnowTag id)
        where T : class, IReplicated => RecordsOf<T>().GetValueOrDefault(id);

    public IReadOnlyList<T> Get<T>(IEnumerable<SnowTag> ids)
        where T : class, IReplicated
    {
        var records = RecordsOf<T>();
        return ids.Select(id => records.GetValueOrDefault(id))
            .Where(r => r != null && !r.Deleted)
            .ToArray();
    }

    public IReadOnlyList<T> Get<T>(Func<T, bool> filter)
        where T : class, IReplicated =>
        RecordsOf<T>().Values.Where(r => !r.Deleted && filter(r)).ToArray();

    public IReadOnlyList<T> Get<T>()
        where T : class, IReplicated => RecordsOf<T>().Values.Where(r => !r.Deleted).ToArray();

    public T Value<T>()
        where T : class =>
        ContainerOf(typeof(T)) is ReplicatedValue<T> v
            ? v.Value
            : throw new InvalidOperationException($"{typeof(T).Name} is not in a value");

    /// <summary>Always throws, since only a watch has a previous run to compare against.</summary>
    public Projection<K> Project<T, K>(Func<IRecordReader, T, K?> projection)
        where T : class, IReplicated
        where K : struct
    {
        throw new InvalidOperationException(
            "Project can only be called on the IRecordReader provided to the Watch(fn)."
        );
    }

    #endregion
}

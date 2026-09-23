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

    private void FlushWatchers()
    {
        var batch = _dirty.ToArray();
        _dirty.Clear();

        foreach (var watcher in batch)
        {
            if (_watchers.Contains(watcher) && IsInstanceValid(watcher.Owner))
                watcher.Run();
        }
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

    T IRecordReader.Get<T>(SnowTag id) =>
        RecordsOf<T>().TryGetValue(id, out var r) && !r.Deleted ? r : null;

    IReadOnlyList<T> IRecordReader.Get<T>(IEnumerable<SnowTag> ids)
    {
        var records = RecordsOf<T>();
        return ids.Select(id => records.GetValueOrDefault(id))
            .Where(r => r != null && !r.Deleted)
            .ToArray();
    }

    IReadOnlyList<T> IRecordReader.Get<T>(Func<T, bool> filter) =>
        RecordsOf<T>().Values.Where(r => !r.Deleted && filter(r)).ToArray();

    IReadOnlyList<T> IRecordReader.Get<T>() =>
        RecordsOf<T>().Values.Where(r => !r.Deleted).ToArray();

    T IRecordReader.Value<T>() =>
        ContainerOf(typeof(T)) is ReplicatedValue<T> v
            ? v.Value
            : throw new InvalidOperationException($"{typeof(T).Name} is not in a value");

    #endregion
}

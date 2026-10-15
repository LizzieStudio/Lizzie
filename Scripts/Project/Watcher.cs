using System;
using System.Collections.Generic;
using Godot;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Runs a callback and records every record it reads so
/// <see cref="RecordService"/> can rerun it when any of them change.
/// </summary>
public sealed class Watcher : IRecordReader
{
    private readonly Action<IRecordReader> _sync;
    private readonly IRecordReader _source;

    // the query values this watcher read
    private readonly DependencyIndex<Watcher> _index;

    // per group key, each atom's id and what Diff projected for it on the previous run
    private readonly Dictionary<(Query, object), object> _diffs = new();

    // group keys Diff compared during the current run
    private readonly HashSet<(Query, object)> _diffed = new();

    public Node Owner { get; }

    private static long _nextSequence;

    /// <summary>Registration order, which breaks ties between watchers at the same depth.</summary>
    public long Sequence { get; } = _nextSequence++;

    public Watcher(
        Node owner,
        Action<IRecordReader> sync,
        IRecordReader source,
        DependencyIndex<Watcher> index
    )
    {
        Owner = owner;
        _sync = sync;
        _source = source;
        _index = index;
    }

    /// <summary>Replaces the dependencies with the ones read by a fresh run.</summary>
    public void Run()
    {
        _index.Clear(this);
        _diffed.Clear();
        try
        {
            _sync(this);
        }
        catch (Exception e)
        {
            GD.PushError($"Watch on {Owner.Name} failed: {e}");
        }
    }

    private void Depend(Query query, object value, bool count = false)
    {
        if (value != null)
            _index.Add(this, query, value, count);
    }

    public T Get<T>(SnowTag id)
        where T : Replicated
    {
        Depend(AtomId.For(typeof(T)), id);
        return _source.Get<T>(id);
    }

    public Replicated Get(SnowTag id)
    {
        foreach (var byId in AtomId.BySnowTag)
            Depend(byId, id);
        return _source.Get(id);
    }

    public T GetIncludingDeleted<T>(SnowTag id)
        where T : Replicated
    {
        Depend(AtomId.For(typeof(T)), id);
        return _source.GetIncludingDeleted<T>(id);
    }

    public T Single<T>()
        where T : Replicated
    {
        Depend(Singleton<T>.All, true);
        return _source.Single<T>();
    }

    public T Get<T, I>(Id<T, I> byId, I id)
        where T : Replicated
    {
        Depend(byId, id);
        return _source.Get(byId, id);
    }

    public T GetIncludingDeleted<T, I>(Id<T, I> byId, I id)
        where T : Replicated
    {
        Depend(byId, id);
        return _source.GetIncludingDeleted(byId, id);
    }

    public IReadOnlyList<T> Get<T, G>(Group<T, G> group, G key)
        where T : Replicated
    {
        Depend(group, key);
        return _source.Get(group, key);
    }

    public int Count<T, G>(Group<T, G> group, G key)
        where T : Replicated
    {
        Depend(group, key, count: true);
        return _source.Count(group, key);
    }

    public GroupDiff<T, V> Diff<T, G, V>(
        Group<T, G> group,
        G key,
        Func<IRecordReader, T, V?> project
    )
        where T : Replicated
        where V : struct
    {
        var slot = (group, (object)key);
        if (!_diffed.Add(slot))
            throw new InvalidOperationException(
                $"Diff was already called for {key} of a {typeof(T).Name} group during this Sync."
            );

        var byId = AtomId.For(typeof(T));
        var previous = _diffs.TryGetValue(slot, out var p)
            ? (Dictionary<object, (T Atom, V Value)>)p
            : new();
        var current = new Dictionary<object, (T Atom, V Value)>();
        foreach (var atom in Get(group, key))
            if (project(this, atom) is V projected)
                current[byId.Of(atom)] = (atom, projected);

        var removed = new List<(T, V)>();
        var added = new List<(T, V)>();
        var comparer = EqualityComparer<V>.Default;

        // a missing entry counts as a null result
        foreach (var (id, old) in previous)
            if (!current.TryGetValue(id, out var now) || !comparer.Equals(old.Value, now.Value))
                removed.Add(old);
        foreach (var (id, now) in current)
            if (!previous.TryGetValue(id, out var old) || !comparer.Equals(old.Value, now.Value))
                added.Add(now);

        _diffs[slot] = current;
        return new GroupDiff<T, V>(removed, added);
    }
}

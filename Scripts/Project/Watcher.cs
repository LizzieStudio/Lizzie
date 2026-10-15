using System;
using System.Collections.Generic;
using System.Linq;
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

    // per record type, predicates that are true for records this watcher read
    private readonly Dictionary<Type, List<Func<object, bool>>> _dependencies = new();

    // per record type, the values its Project returned on the previous run
    private readonly Dictionary<Type, object> _projections = new();

    // record types projected during the current run
    private readonly HashSet<Type> _projected = new();

    // per group key, each atom's id and what GetChanged projected for it on the previous run
    private readonly Dictionary<(Query, object), object> _swaps = new();

    // group values GetChanged compared during the current run
    private readonly HashSet<(Query, object)> _swapped = new();

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

    /// <summary>Whether any change matches a predicate read by the last run.</summary>
    public bool Affected(Type type, IReadOnlyList<RecordChange> changes)
    {
        if (!_dependencies.TryGetValue(type, out var predicates))
            return false;

        // a record that matched before covers updates and removals,
        // and one that matches now covers additions and updates
        foreach (var change in changes)
        {
            foreach (var matches in predicates)
            {
                if (change.Old != null && matches(change.Old))
                    return true;
                if (change.New != null && matches(change.New))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Replaces the dependencies with the ones read by a fresh run.</summary>
    public void Run()
    {
        _index.Clear(this);
        _dependencies.Clear();
        _projected.Clear();
        _swapped.Clear();
        try
        {
            _sync(this);
        }
        catch (Exception e)
        {
            GD.PushError($"Watch on {Owner.Name} failed: {e}");
        }
    }

    private void Depend(Type type, Func<object, bool> matches)
    {
        if (!_dependencies.TryGetValue(type, out var predicates))
            predicates = _dependencies[type] = new();
        predicates.Add(matches);
    }

    private void Depend<T>(Func<T, bool> matches)
        where T : Replicated =>
        Depend(typeof(T), o => o is T record && !record.Deleted && matches(record));

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

    public IReadOnlyList<T> Get<T>(IEnumerable<SnowTag> ids)
        where T : Replicated
    {
        var list = ids.ToArray();
        foreach (var id in list)
            Depend(AtomId.For(typeof(T)), id);
        return _source.Get<T>(list);
    }

    public IReadOnlyList<T> Get<T>(Func<T, bool> filter)
        where T : Replicated
    {
        Depend(filter);
        return _source.Get(filter);
    }

    public IReadOnlyList<T> GetIncludingDeleted<T>(Func<T, bool> filter)
        where T : Replicated
    {
        Depend(typeof(T), o => o is T record && filter(record));
        return _source.GetIncludingDeleted(filter);
    }

    public IReadOnlyList<T> Get<T>()
        where T : Replicated
    {
        Depend<T>(_ => true);
        return _source.Get<T>();
    }

    public T Single<T>()
        where T : Replicated
    {
        Depend<T>(_ => true);
        return _source.Single<T>();
    }

    public SwapLists<K> GetChanged<T, K>(Func<IRecordReader, T, K?> keyFn)
        where T : Replicated
        where K : struct
    {
        if (!_projected.Add(typeof(T)))
            throw new InvalidOperationException(
                $"GetChanged was already called for {typeof(T).Name} during this Sync."
            );

        var records = Get<T>();
        var previous = _projections.TryGetValue(typeof(T), out var p)
            ? (Dictionary<SnowTag, K>)p
            : new Dictionary<SnowTag, K>();
        var current = new Dictionary<SnowTag, K>();
        foreach (var record in records)
            if (keyFn(this, record) is K value)
                current[record.Id] = value;

        var deleted = new List<(SnowTag, K)>();
        var created = new List<(SnowTag, K)>();
        var comparer = EqualityComparer<K>.Default;

        // a missing entry counts as a null key
        foreach (var (id, old) in previous)
            if (!current.TryGetValue(id, out var value) || !comparer.Equals(old, value))
                deleted.Add((id, old));
        foreach (var (id, value) in current)
            if (!previous.TryGetValue(id, out var old) || !comparer.Equals(old, value))
                created.Add((id, value));

        _projections[typeof(T)] = current;
        return new SwapLists<K>(deleted, created);
    }

    public SwapLists GetChanged<T>()
        where T : Replicated
    {
        var (deleted, created) = GetChanged<T, bool>((R, value) => true);
        return new SwapLists(
            deleted.Select(d => d.Id).ToArray(),
            created.Select(c => c.Id).ToArray()
        );
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

    public IReadOnlyList<T> GetIncludingDeleted<T, G>(Group<T, G> group, G key)
        where T : Replicated
    {
        Depend(group, key);
        return _source.GetIncludingDeleted(group, key);
    }

    public int Count<T, G>(Group<T, G> group, G key)
        where T : Replicated
    {
        Depend(group, key, count: true);
        return _source.Count(group, key);
    }

    public SwapLists<T, V> GetChanged<T, G, V>(
        Group<T, G> group,
        G key,
        Func<IRecordReader, T, V?> project
    )
        where T : Replicated
        where V : struct
    {
        var slot = (group, (object)key);
        if (!_swapped.Add(slot))
            throw new InvalidOperationException(
                $"GetChanged was already called for {key} of a {typeof(T).Name} group during this Sync."
            );

        var byId = AtomId.For(typeof(T));
        var previous = _swaps.TryGetValue(slot, out var p)
            ? (Dictionary<object, (T Atom, V Value)>)p
            : new();
        var current = new Dictionary<object, (T Atom, V Value)>();
        foreach (var atom in Get(group, key))
            if (project(this, atom) is V projected)
                current[byId.Of(atom)] = (atom, projected);

        var deleted = new List<(T, V)>();
        var created = new List<(T, V)>();
        var comparer = EqualityComparer<V>.Default;

        // a missing entry counts as a null result
        foreach (var (id, old) in previous)
            if (!current.TryGetValue(id, out var now) || !comparer.Equals(old.Value, now.Value))
                deleted.Add(old);
        foreach (var (id, now) in current)
            if (!previous.TryGetValue(id, out var old) || !comparer.Equals(old.Value, now.Value))
                created.Add(now);

        _swaps[slot] = current;
        return new SwapLists<T, V>(deleted, created);
    }
}

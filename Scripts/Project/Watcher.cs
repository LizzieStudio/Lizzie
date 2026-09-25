using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Runs a callback and records every record it reads so
/// <see cref="ProjectService"/> can rerun it when any of them change.
/// </summary>
public sealed class Watcher : IRecordReader
{
    private readonly Action<IRecordReader> _sync;
    private readonly IRecordReader _source;

    // per record type, predicates that are true for records this watcher read
    private readonly Dictionary<Type, List<Func<object, bool>>> _dependencies = new();

    // per record type, the values its Project returned on the previous run
    private readonly Dictionary<Type, object> _projections = new();

    // record types projected during the current run
    private readonly HashSet<Type> _projected = new();

    public Node Owner { get; }

    public Watcher(Node owner, Action<IRecordReader> sync, IRecordReader source)
    {
        Owner = owner;
        _sync = sync;
        _source = source;
    }

    /// <summary>Whether any change touches a record read by the last run.</summary>
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
        _dependencies.Clear();
        _projected.Clear();
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
        where T : class, IReplicated =>
        Depend(typeof(T), o => o is T record && !record.Deleted && matches(record));

    public T Get<T>(SnowTag id)
        where T : class, IReplicated
    {
        Depend<T>(r => r.Id == id);
        return _source.Get<T>(id);
    }

    public T GetIncludingDeleted<T>(SnowTag id)
        where T : class, IReplicated
    {
        Depend(typeof(T), o => o is T record && record.Id == id);
        return _source.GetIncludingDeleted<T>(id);
    }

    public IReadOnlyList<T> Get<T>(IEnumerable<SnowTag> ids)
        where T : class, IReplicated
    {
        var list = ids.ToArray();
        var set = list.ToHashSet();
        Depend<T>(r => set.Contains(r.Id));
        return _source.Get<T>(list);
    }

    public IReadOnlyList<T> Get<T>(Func<T, bool> filter)
        where T : class, IReplicated
    {
        Depend(filter);
        return _source.Get(filter);
    }

    public IReadOnlyList<T> Get<T>()
        where T : class, IReplicated
    {
        Depend<T>(_ => true);
        return _source.Get<T>();
    }

    public T Value<T>()
        where T : class
    {
        Depend(typeof(T), _ => true);
        return _source.Value<T>();
    }

    public SwapLists<K> GetChanged<T, K>(Func<IRecordReader, T, K?> keyFn)
        where T : class, IReplicated
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
        where T : class, IReplicated
    {
        var (deleted, created) = GetChanged<T, bool>((R, value) => true);
        return new SwapLists(
            deleted.Select(d => d.Id).ToArray(),
            created.Select(c => c.Id).ToArray()
        );
    }
}

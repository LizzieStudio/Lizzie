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
}

using System;
using System.Collections.Generic;
using Godot;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Runs a callback and records every atom it reads so
/// <see cref="Atoms"/> can rerun it when any of them change.
/// </summary>
public sealed class Watcher : IWatchReader
{
    private readonly Action<IWatchReader> _sync;
    private readonly IAtomReader _source;

    // the query values this watcher read
    private readonly DependencyIndex<Watcher> _index;

    // per lookup key and Basis, each atom's id and the basis Diff found for it when the previous run ended
    private readonly Dictionary<(Query, object, object), object> _diffs = new();

    // the bases Diff found during the current run, saved into _diffs when it ends
    private readonly Dictionary<(Query, object, object), object> _pending = new();

    public Node Owner { get; }

    private static long _nextSequence;

    /// <summary>Registration order, which breaks ties between watchers at the same depth.</summary>
    public long Sequence { get; } = _nextSequence++;

    public Watcher(
        Node owner,
        Action<IWatchReader> sync,
        IAtomReader source,
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
        try
        {
            _sync(this);
        }
        catch (Exception e)
        {
            GD.PushError($"Watch on {Owner.Name} failed: {e}");
        }
        foreach (var (slot, bases) in _pending)
            _diffs[slot] = bases;
        _pending.Clear();
    }

    private void Depend(Query query, object value, bool count = false)
    {
        if (value != null)
            _index.Add(this, query, value, count);
    }

    public Atom Get(IAtomRef atom)
    {
        Depend(IdQuery.For(atom.AtomType), atom);
        return _source.Get(atom);
    }

    public SnowportId Version(Atom atom)
    {
        var byId = IdQuery.For(atom.GetType());
        Depend(byId, byId.Of(atom));
        return _source.Version(atom);
    }

    public T Get<T, I>(Identity<T, I> byId, I id)
        where T : Atom
        where I : IAtomRef<T>, IComparable<I>
    {
        Depend(byId, id);
        return _source.Get(byId, id);
    }

    public List<T> Get<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom
    {
        Depend(lookup, key);
        return _source.Get(lookup, key);
    }

    public int Count<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom
    {
        Depend(lookup, key, count: true);
        return _source.Count(lookup, key);
    }

    public LookupDiff<(T Atom, V Value)> Diff<T, K, V>(
        Lookup<T, K> lookup,
        K key,
        Basis<T, V> basis
    )
        where T : Atom
        where V : struct
    {
        var slot = (lookup, (object)key, (object)basis);
        var byId = IdQuery.For(typeof(T));
        var previous = _diffs.TryGetValue(slot, out var p)
            ? (Dictionary<object, (T Atom, V Value)>)p
            : new();
        var current = new Dictionary<object, (T Atom, V Value)>();
        foreach (var atom in Get(lookup, key))
            if (basis.Of(this, atom) is V value)
                current[byId.Of(atom)] = (atom, value);

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

        _pending[slot] = current;
        return new LookupDiff<(T Atom, V Value)>(removed, added);
    }
}

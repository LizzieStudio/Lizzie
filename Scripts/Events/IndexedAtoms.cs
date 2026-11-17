using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// A store's atoms of <typeparamref name="T"/> by id, with an index for each lookup read so far,
/// and the query values a change of one of them touches.
/// </summary>
internal sealed class IndexedAtoms<T, I>(Identity<T, I> byId)
    where T : Atom
    where I : IAtomRef<T>, IComparable<I>
{
    private readonly Dictionary<I, AtomEntry> _atoms = new();

    // each lookup read so far, kept up to date from then on
    private readonly Dictionary<Query, IIndex> _indexes = new();

    public IReadOnlyDictionary<I, AtomEntry> Entries => _atoms;

    /// <summary>
    /// The atom with <paramref name="id"/>, or an entry with a null value if there's none.
    /// </summary>
    public AtomEntry Find(I id) => _atoms.GetValueOrDefault(id);

    public List<T> Get<K>(Lookup<T, K> lookup, K key) =>
        key is null ? [] : IndexOf(lookup).Atoms(key);

    public int Count<K>(Lookup<T, K> lookup, K key) => key is null ? 0 : IndexOf(lookup).Count(key);

    private Index<K> IndexOf<K>(Lookup<T, K> lookup)
    {
        if (_indexes.TryGetValue(lookup, out var found))
            return (Index<K>)found;

        var index = new Index<K>(lookup);
        foreach (var (id, entry) in _atoms)
            index.Add(id, entry);
        _indexes[lookup] = index;
        return index;
    }

    /// <summary>
    /// Puts <paramref name="now"/> in place of the atom with <paramref name="id"/>,
    /// or removes it when <paramref name="now"/> has a null value.
    /// </summary>
    public void Replace(I id, AtomEntry now)
    {
        var old = _atoms.GetValueOrDefault(id);
        if (now.Value == null)
            _atoms.Remove(id);
        else
            _atoms[id] = now;

        foreach (var index in _indexes.Values)
        {
            if (old.Value != null)
                index.Remove(id, old);
            if (now.Value != null)
                index.Add(id, now);
        }
    }

    public void Clear()
    {
        _atoms.Clear();
        foreach (var index in _indexes.Values)
            index.Clear();
    }

    /// <summary>
    /// The id and the lookup keys a change of the atom with <paramref name="id"/> touched.
    /// </summary>
    public IEnumerable<Touch> Touched(I id, AtomEntry old, AtomEntry now)
    {
        yield return new Touch(byId, id, false);
        foreach (var index in _indexes.Values)
        foreach (var touch in index.Touched(old, now))
            yield return touch;
    }

    private interface IIndex
    {
        void Add(I id, AtomEntry entry);

        void Remove(I id, AtomEntry entry);

        void Clear();

        IEnumerable<Touch> Touched(AtomEntry old, AtomEntry now);
    }

    /// <summary>
    /// The atoms with each key of a lookup, leaving out unset ones.
    /// </summary>
    private sealed class Index<K>(Lookup<T, K> lookup) : IIndex
    {
        private readonly Dictionary<K, Dictionary<I, T>> _buckets = new();

        // each bucket in the lookup's order, from its first read until it changes
        private readonly Dictionary<K, List<T>> _ordered = new();

        public List<T> Atoms(K key)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
                return [];
            if (!_ordered.TryGetValue(key, out var ordered))
                _ordered[key] = ordered = lookup.Ordered(bucket.Values);
            return [.. ordered];
        }

        public int Count(K key) => _buckets.GetValueOrDefault(key)?.Count ?? 0;

        public void Add(I id, AtomEntry entry)
        {
            if (!In(entry, out var key))
                return;
            if (!_buckets.TryGetValue(key, out var bucket))
                _buckets[key] = bucket = new();
            bucket[id] = (T)entry.Value;
            _ordered.Remove(key);
        }

        public void Remove(I id, AtomEntry entry)
        {
            if (!In(entry, out var key))
                return;
            var bucket = _buckets[key];
            bucket.Remove(id);
            if (bucket.Count == 0)
                _buckets.Remove(key);
            _ordered.Remove(key);
        }

        public void Clear()
        {
            _buckets.Clear();
            _ordered.Clear();
        }

        /// <summary>
        /// The lookup keys whose atoms a change touched, and the lookup keys whose count it changed.
        /// </summary>
        public IEnumerable<Touch> Touched(AtomEntry old, AtomEntry now)
        {
            bool inOld = In(old, out var was);
            bool inNew = In(now, out var willBe);
            bool same = inOld && inNew && EqualityComparer<K>.Default.Equals(was, willBe);

            if (inOld)
                yield return new Touch(lookup, was, false);
            if (inNew && !same)
                yield return new Touch(lookup, willBe, false);

            if (same)
                yield break;
            if (inOld)
                yield return new Touch(lookup, was, true);
            if (inNew)
                yield return new Touch(lookup, willBe, true);
        }

        private bool In(AtomEntry entry, out K key)
        {
            key = entry.Value == null ? default : lookup.Of((T)entry.Value);
            return entry.Value != null && key is not null;
        }
    }
}

public static class LookupMachinery
{
    /// <summary>
    /// <paramref name="atoms"/> in <paramref name="lookup"/>'s order, with ties in id order,
    /// or as they come when it has none.
    /// </summary>
    public static List<T> Ordered<T, K>(this Lookup<T, K> lookup, IEnumerable<T> atoms)
        where T : Atom
    {
        var ordered = atoms.ToList();
        if (lookup.Order is { } order)
        {
            var ids = IdQuery.For(typeof(T));
            ordered.Sort((a, b) => order(a, b) is var c and not 0 ? c : ids.CompareIds(a, b));
        }
        return ordered;
    }
}

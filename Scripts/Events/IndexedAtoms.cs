using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// A store's atoms of <typeparamref name="T"/> by id, with an index for each group read so far,
/// and the query values a change of one of them touches.
/// </summary>
internal sealed class IndexedAtoms<T, I>(Id<T, I> byId)
    where T : Atom
    where I : IComparable<I>
{
    private readonly Dictionary<I, AtomEntry> _atoms = new();

    // each group read so far, kept up to date from then on
    private readonly Dictionary<Query, IIndex> _indexes = new();

    public IReadOnlyDictionary<I, AtomEntry> Entries => _atoms;

    /// <summary>
    /// The atom with <paramref name="id"/>, or an entry with a null value if there's none.
    /// </summary>
    public AtomEntry Find(I id) => _atoms.GetValueOrDefault(id);

    public IReadOnlyList<T> InGroup<G>(Group<T, G> group, G key) =>
        key is null ? [] : IndexOf(group).Atoms(key);

    public int Count<G>(Group<T, G> group, G key) => key is null ? 0 : IndexOf(group).Count(key);

    private Index<G> IndexOf<G>(Group<T, G> group)
    {
        if (_indexes.TryGetValue(group, out var found))
            return (Index<G>)found;

        var index = new Index<G>(group);
        foreach (var (id, entry) in _atoms)
            index.Add(id, entry);
        _indexes[group] = index;
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
    /// The id and the group keys a change of the atom with <paramref name="id"/> touched.
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
    /// The atoms with each key of a group, leaving out unset ones.
    /// </summary>
    private sealed class Index<G>(Group<T, G> group) : IIndex
    {
        private readonly Dictionary<G, Dictionary<I, T>> _buckets = new();

        public IReadOnlyList<T> Atoms(G key) =>
            _buckets.TryGetValue(key, out var bucket) ? bucket.Values.ToArray() : [];

        public int Count(G key) => _buckets.GetValueOrDefault(key)?.Count ?? 0;

        public void Add(I id, AtomEntry entry)
        {
            if (!In(entry, out var key))
                return;
            if (!_buckets.TryGetValue(key, out var bucket))
                _buckets[key] = bucket = new();
            bucket[id] = (T)entry.Value;
        }

        public void Remove(I id, AtomEntry entry)
        {
            if (!In(entry, out var key))
                return;
            var bucket = _buckets[key];
            bucket.Remove(id);
            if (bucket.Count == 0)
                _buckets.Remove(key);
        }

        public void Clear() => _buckets.Clear();

        /// <summary>
        /// The group keys whose atoms a change touched, and the group keys whose count it changed.
        /// </summary>
        public IEnumerable<Touch> Touched(AtomEntry old, AtomEntry now)
        {
            bool inOld = In(old, out var was);
            bool inNew = In(now, out var willBe);
            bool same = inOld && inNew && EqualityComparer<G>.Default.Equals(was, willBe);

            if (inOld)
                yield return new Touch(group, was, false);
            if (inNew && !same)
                yield return new Touch(group, willBe, false);

            if (same)
                yield break;
            if (inOld)
                yield return new Touch(group, was, true);
            if (inNew)
                yield return new Touch(group, willBe, true);
        }

        private bool In(AtomEntry entry, out G key)
        {
            key = entry.Value == null ? default : group.Of((T)entry.Value);
            return entry.Value != null && key is not null;
        }
    }
}

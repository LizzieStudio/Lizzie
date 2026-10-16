using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Holds the atoms of <typeparamref name="T"/> by their <see cref="Id{T, I}"/>,
/// and any groups that have been read.
/// </summary>
public sealed class AtomStore<T, I> : IAtomStore<T>
    where T : Replicated
{
    private static readonly bool Saved = !typeof(T).IsDefined(typeof(NotSavedAttribute), true);

    private readonly Id<T, I> _byId;

    private readonly Dictionary<I, AtomEntry> _atoms = new();

    // each group read so far, kept up to date from then on
    private readonly Dictionary<Query, IIndex> _indexes = new();

    // changes merged while the synchronizer is bulk loading, sent by FlushBulkLoad
    private Dictionary<I, (AtomEntry Old, AtomEntry New)> _pending = new();

    public AtomStore(Id<T, I> byId) => _byId = byId;

    public Type RecordType => typeof(T);

    public event Action<IReadOnlyList<Touch>> Changed;

    /// <summary>
    /// The atom with <paramref name="id"/>, or an entry with a null value if it was never written.
    /// </summary>
    public AtomEntry Find(I id) => _atoms.GetValueOrDefault(id);

    AtomEntry IReplicatedStore.Find(object id) => id is I typed ? Find(typed) : default;

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

    public void Apply(EventLog log, IReadOnlyList<TableEvent> changed, bool bulkLoading)
    {
        var ids = changed
            .SelectMany(c => c.Records.Concat(c.Unsets))
            .OfType<T>()
            .Select(_byId.Of)
            .ToHashSet();
        if (ids.Count == 0)
            return;

        // The newest in-effect change of each id decides it: a write is its value, and an unset leaves it out.
        var latest = new Dictionary<I, AtomEntry>();
        var undecided = new HashSet<I>(ids);
        foreach (var e in UndoLog.InEffect(log))
        {
            // unsets first, since an unset beats a write in the same event
            foreach (var atom in e.Unsets.OfType<T>())
                undecided.Remove(_byId.Of(atom));
            for (int j = e.Records.Length - 1; j >= 0; j--)
                if (e.Records[j] is T atom && undecided.Remove(_byId.Of(atom)))
                    latest[_byId.Of(atom)] = new AtomEntry(atom, e.Id);
            if (undecided.Count == 0)
                break;
        }

        var merged = new Dictionary<I, (AtomEntry Old, AtomEntry New)>();
        foreach (var id in ids)
        {
            var now = latest.GetValueOrDefault(id);
            var old = _atoms.GetValueOrDefault(id);
            if (old == now)
                continue;
            Replace(id, old, now);
            merged[id] = (old, now);
        }

        if (merged.Count == 0)
            return;
        if (bulkLoading)
        {
            // keeps the value from before the bulk load began
            foreach (var (id, change) in merged)
                _pending[id] = _pending.TryGetValue(id, out var first)
                    ? (first.Old, change.New)
                    : change;
            return;
        }
        Notify(merged);
    }

    private void Replace(I id, AtomEntry old, AtomEntry now)
    {
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

    private void Notify(IReadOnlyDictionary<I, (AtomEntry Old, AtomEntry New)> changed) =>
        Changed?.Invoke(
            changed.SelectMany(c => Touched(c.Key, c.Value.Old, c.Value.New)).ToArray()
        );

    public void FlushBulkLoad()
    {
        if (_pending.Count == 0)
            return;

        var changed = _pending;
        _pending = new();
        Notify(changed);
    }

    public void Clear()
    {
        var removed = _atoms.ToDictionary(a => a.Key, a => (a.Value, default(AtomEntry)));
        _atoms.Clear();
        foreach (var index in _indexes.Values)
            index.Clear();
        _pending.Clear();

        Notify(removed);
    }

    private IEnumerable<Touch> Touched(I id, AtomEntry old, AtomEntry now)
    {
        yield return new Touch(_byId, id, false);
        foreach (var index in _indexes.Values)
        foreach (var touch in index.Touched(old, now))
            yield return touch;
    }

    public IEnumerable<Replicated> SavedAtoms() => Saved ? _atoms.Values.Select(e => e.Value) : [];

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

/// <summary>
/// A query and a key of it (an id, or a group key) whose result a change touched.
/// When <paramref name="Count"/> is true, it changed how many atoms the group key has rather than what they hold.
/// </summary>
public readonly record struct Touch(Query Query, object Key, bool Count);

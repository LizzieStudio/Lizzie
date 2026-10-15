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

    private readonly Dictionary<I, T> _atoms = new();

    // each group read so far, kept up to date from then on
    private readonly Dictionary<Query, IIndex> _indexes = new();

    // changes merged while the synchronizer is bulk loading, sent by FlushBulkLoad
    private Dictionary<I, (T Old, T New)> _pending = new();

    public AtomStore(Id<T, I> byId) => _byId = byId;

    public Type RecordType => typeof(T);

    public event Action<IReadOnlyList<RecordChange>> Changed;

    /// <summary>
    /// The atom with <paramref name="id"/>, even if it is deleted, or null.
    /// </summary>
    public T Find(I id) => _atoms.GetValueOrDefault(id);

    Replicated IReplicatedStore.Find(SnowTag tag) => tag is I id ? Find(id) : null;

    public IEnumerable<T> All => _atoms.Values;

    public IReadOnlyCollection<T> InGroup<G>(Group<T, G> group, G key) =>
        key is null ? [] : IndexOf(group).Atoms(key);

    public int Count<G>(Group<T, G> group, G key) => key is null ? 0 : IndexOf(group).Count(key);

    private Index<G> IndexOf<G>(Group<T, G> group)
    {
        if (_indexes.TryGetValue(group, out var found))
            return (Index<G>)found;

        var index = new Index<G>(group);
        foreach (var (id, atom) in _atoms)
            index.Add(id, atom);
        _indexes[group] = index;
        return index;
    }

    public void Apply(EventLog log, IReadOnlyList<TableEvent> changed, bool bulkLoading)
    {
        var ids = changed.SelectMany(c => c.Records.OfType<T>()).Select(_byId.Of).ToHashSet();
        if (ids.Count == 0)
            return;

        var latest = ids.ToDictionary(id => id, _ => (T)null);
        var unfound = new HashSet<I>(ids);
        foreach (var writer in UndoLog.InEffect(log))
        {
            for (int j = writer.Records.Length - 1; j >= 0; j--)
                if (writer.Records[j] is T atom && unfound.Remove(_byId.Of(atom)))
                    latest[_byId.Of(atom)] = atom with { LastUpdateId = writer.Id };
            if (unfound.Count == 0)
                break;
        }

        var merged = new Dictionary<I, (T Old, T New)>();
        foreach (var (id, now) in latest)
        {
            var old = _atoms.GetValueOrDefault(id);
            if (Equals(old, now))
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

    private void Replace(I id, T old, T now)
    {
        if (now == null)
            _atoms.Remove(id);
        else
            _atoms[id] = now;

        foreach (var index in _indexes.Values)
        {
            if (old != null)
                index.Remove(id, old);
            if (now != null)
                index.Add(id, now);
        }
    }

    private void Notify(IReadOnlyDictionary<I, (T Old, T New)> changed) =>
        Changed?.Invoke(changed.Values.Select(c => new RecordChange(c.Old, c.New)).ToArray());

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
        var removed = _atoms.Values.ToArray();
        _atoms.Clear();
        foreach (var index in _indexes.Values)
            index.Clear();
        _pending.Clear();

        Changed?.Invoke(removed.Select(r => new RecordChange(r, null)).ToArray());
    }

    public IEnumerable<Touch> Touched(IReadOnlyList<RecordChange> changes)
    {
        foreach (var (old, now) in changes)
        {
            yield return new Touch(_byId, _byId.Of((T)(old ?? now)), false);
            foreach (var index in _indexes.Values)
            foreach (var touch in index.Touched((T)old, (T)now))
                yield return touch;
        }
    }

    public IEnumerable<Replicated> SavedRecords() => Saved ? _atoms.Values : [];

    private interface IIndex
    {
        void Add(I id, T atom);

        void Remove(I id, T atom);

        void Clear();

        IEnumerable<Touch> Touched(T old, T now);
    }

    /// <summary>
    /// The atoms with each key of a group, deleted ones included, and how many of them aren't deleted.
    /// </summary>
    private sealed class Index<G>(Group<T, G> group) : IIndex
    {
        private readonly Dictionary<G, Dictionary<I, T>> _buckets = new(group.Comparer);
        private readonly Dictionary<G, int> _live = new(group.Comparer);

        public IReadOnlyCollection<T> Atoms(G key) =>
            _buckets.TryGetValue(key, out var bucket) ? bucket.Values : [];

        public int Count(G key) => _live.GetValueOrDefault(key);

        public void Add(I id, T atom)
        {
            if (!In(atom, out var key))
                return;
            if (!_buckets.TryGetValue(key, out var bucket))
                _buckets[key] = bucket = new();
            bucket[id] = atom;
            if (!atom.Deleted)
                _live[key] = _live.GetValueOrDefault(key) + 1;
        }

        public void Remove(I id, T atom)
        {
            if (!In(atom, out var key))
                return;
            var bucket = _buckets[key];
            bucket.Remove(id);
            if (bucket.Count == 0)
                _buckets.Remove(key);
            if (!atom.Deleted && --_live[key] == 0)
                _live.Remove(key);
        }

        public void Clear()
        {
            _buckets.Clear();
            _live.Clear();
        }

        /// <summary>
        /// The group keys whose atoms a change touched, and the group keys whose count of atoms that aren't deleted it changed.
        /// </summary>
        public IEnumerable<Touch> Touched(T old, T now)
        {
            bool inOld = In(old, out var was);
            bool inNew = In(now, out var willBe);
            bool same = inOld && inNew && group.Comparer.Equals(was, willBe);

            if (inOld)
                yield return new Touch(group, was, false);
            if (inNew && !same)
                yield return new Touch(group, willBe, false);

            bool liveOld = inOld && !old.Deleted;
            bool liveNew = inNew && !now.Deleted;
            if (liveOld && liveNew && same)
                yield break;
            if (liveOld)
                yield return new Touch(group, was, true);
            if (liveNew)
                yield return new Touch(group, willBe, true);
        }

        private bool In(T atom, out G key)
        {
            key = atom == null ? default : group.Of(atom);
            return key is not null;
        }
    }
}

/// <summary>
/// A query and a key of it (an id, or a group key) whose result a change touched.
/// When <paramref name="Count"/> is true, it changed how many atoms the group key has rather than what they hold.
/// </summary>
public readonly record struct Touch(Query Query, object Key, bool Count);

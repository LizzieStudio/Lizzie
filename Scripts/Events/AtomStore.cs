using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Holds the atoms of <typeparamref name="T"/> by their <see cref="Identity{T, I}"/>,
/// and any lookups that have been read.
/// </summary>
public sealed class AtomStore<T, I> : IAtomStore<T, I>
    where T : Atom
    where I : IAtomRef<T>, IComparable<I>
{
    private static readonly bool Saved = !typeof(T).IsDefined(typeof(NotSavedAttribute), true);

    private readonly Identity<T, I> _byId;

    private readonly IndexedAtoms<T, I> _atoms;

    // changes merged while the synchronizer is bulk loading, sent by FlushBulkLoad
    private Dictionary<I, (AtomEntry Old, AtomEntry New)> _pending = new();

    public AtomStore(Identity<T, I> byId)
    {
        _byId = byId;
        _atoms = new(byId);
    }

    public Type AtomType => typeof(T);

    public event Action<IReadOnlyList<Touch>> Changed;

    /// <summary>
    /// The atom with <paramref name="id"/>, or an entry with a null value if it was never written.
    /// </summary>
    public AtomEntry Find(I id) => _atoms.Find(id);

    AtomEntry IAtomStore.Find(object id) => id is I typed ? Find(typed) : default;

    /// <summary>
    /// The ids of the atoms held.
    /// </summary>
    internal IEnumerable<I> Ids => _atoms.Entries.Keys;

    public List<T> Get<K>(Lookup<T, K> lookup, K key) => _atoms.Get(lookup, key);

    public int Count<K>(Lookup<T, K> lookup, K key) => _atoms.Count(lookup, key);

    public void Apply(Timeline timeline, IReadOnlyList<TimelineEvent> changed, bool bulkLoading)
    {
        var unsets = changed
            .SelectMany(c => c.Unsets)
            .Where(u => u.Query.AtomType == typeof(T))
            .ToList();
        // A lookup unset may cover any atom of the type, even one the store no longer holds.
        bool all = unsets.Exists(u => u.Query is not IdQuery);
        var ids = changed
            .SelectMany(c => c.Writes)
            .OfType<T>()
            .Select(_byId.Of)
            .Concat(unsets.Where(u => u.Query is IdQuery).Select(u => (I)u.Key))
            .ToHashSet();
        if (all)
            ids.UnionWith(_atoms.Entries.Keys);
        else if (ids.Count == 0)
            return;

        var latest = Latest(timeline, all ? null : ids, TimelineUndo.Counts(timeline));
        if (all)
            ids.UnionWith(latest.Keys);

        var merged = new Dictionary<I, (AtomEntry Old, AtomEntry New)>();
        foreach (var id in ids)
        {
            var now = latest.GetValueOrDefault(id);
            var old = _atoms.Find(id);
            if (old == now)
                continue;
            _atoms.Replace(id, now);
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

    /// <summary>
    /// Gets all atoms of <paramref name="ids"/>, or every id for null, that passes <paramref name="filter"/>.
    /// </summary>
    private Dictionary<I, AtomEntry> Latest(
        Timeline timeline,
        HashSet<I> ids,
        Func<Atom, bool> filter
    )
    {
        var latest = new Dictionary<I, AtomEntry>();
        var decided = new HashSet<I>();
        var newer = new List<Unset>();
        foreach (var e in TimelineUndo.InEffect(timeline))
        {
            for (int j = e.Writes.Length - 1; j >= 0; j--)
            {
                if (e.Writes[j] is not T atom || !filter(atom))
                    continue;
                var id = _byId.Of(atom);
                if (
                    (ids == null || ids.Contains(id))
                    && decided.Add(id)
                    && !newer.Exists(u => u.Matches(atom))
                )
                    latest[id] = new AtomEntry(atom, e.Id);
            }
            // after the writes, since a write wins over an unset in the same event
            newer.AddRange(e.Unsets.Where(u => u.Query.AtomType == typeof(T)));
            if (ids != null && decided.Count == ids.Count)
                break;
        }
        return latest;
    }

    private void Notify(IReadOnlyDictionary<I, (AtomEntry Old, AtomEntry New)> changed) =>
        Changed?.Invoke(
            changed.SelectMany(c => _atoms.Touched(c.Key, c.Value.Old, c.Value.New)).ToArray()
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
        var removed = _atoms.Entries.ToDictionary(a => a.Key, a => (a.Value, default(AtomEntry)));
        _atoms.Clear();
        _pending.Clear();

        Notify(removed);
    }

    public IEnumerable<AtomEntry> SavedAtoms(Timeline timeline)
    {
        if (!Saved)
            return [];
        var drafts = _atoms
            .Entries.Where(a => a.Value.Value.Transition != Transition.None)
            .Select(a => a.Key)
            .ToHashSet();
        var committed = Latest(timeline, drafts, write => write.Transition == Transition.None);
        return _atoms
            .Entries.Values.Where(e => e.Value.Transition == Transition.None)
            .Concat(committed.Values);
    }
}

/// <summary>
/// A query and a key of it (an id, or a lookup key) whose result a change touched.
/// When <paramref name="Count"/> is true, it changed how many atoms the key has rather than what they hold.
/// </summary>
public readonly record struct Touch(Query Query, object Key, bool Count);

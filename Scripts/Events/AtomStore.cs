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
        var ids = changed
            .SelectMany(c => c.Writes.Concat(c.Unsets))
            .OfType<T>()
            .Select(_byId.Of)
            .ToHashSet();
        if (ids.Count == 0)
            return;

        // The newest in-effect change of each id decides it: a write is its value, and an unset leaves it out.
        var latest = new Dictionary<I, AtomEntry>();
        var undecided = new HashSet<I>(ids);
        foreach (var e in TimelineUndo.InEffect(timeline))
        {
            // unsets first, since an unset beats a write in the same event
            foreach (var atom in e.Unsets.OfType<T>())
                undecided.Remove(_byId.Of(atom));
            for (int j = e.Writes.Length - 1; j >= 0; j--)
                if (e.Writes[j] is T atom && undecided.Remove(_byId.Of(atom)))
                    latest[_byId.Of(atom)] = new AtomEntry(atom, e.Id);
            if (undecided.Count == 0)
                break;
        }

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

    public IEnumerable<AtomEntry> SavedAtoms() => Saved ? _atoms.Entries.Values : [];
}

/// <summary>
/// A query and a key of it (an id, or a lookup key) whose result a change touched.
/// When <paramref name="Count"/> is true, it changed how many atoms the key has rather than what they hold.
/// </summary>
public readonly record struct Touch(Query Query, object Key, bool Count);

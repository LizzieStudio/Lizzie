using System;
using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Holds the current atoms of one type, worked out from the timeline.
/// <see cref="Atoms"/> makes one for each type the first time it's read or written.
/// </summary>
public interface IAtomStore
{
    /// <summary>The type of atom held, unique across stores.</summary>
    Type AtomType { get; }

    /// <summary>
    /// Invoked with the queries and values whose results a change touched.
    /// </summary>
    event Action<IReadOnlyList<Touch>> Changed;

    /// <summary>
    /// Merges an event that was just recorded in <paramref name="timeline"/>.
    /// <paramref name="changed"/> are the events whose atoms it puts in or out of effect:
    /// the event itself, or what an undo or redo reverses.
    /// While <paramref name="bulkLoading"/>, the changes wait for <see cref="FlushBulkLoad"/>.
    /// </summary>
    void Apply(Timeline timeline, IReadOnlyList<TimelineEvent> changed, bool bulkLoading);

    /// <summary>
    /// Works out what changed since the last Settle call.
    /// </summary>
    void Settle() { }

    /// <summary>Removes everything, e.g. when the project is replaced, reporting each removal.</summary>
    void Clear();

    /// <summary>Sends one notification for everything merged during a bulk load.</summary>
    void FlushBulkLoad();

    /// <summary>
    /// The atom with <paramref name="id"/>, or an entry with a null value if this store doesn't hold it.
    /// </summary>
    AtomEntry Find(object id);

    /// <summary>
    /// Every atom that's saved with the project, with the event that last wrote it.
    /// Writes with a transition are not saved.
    /// </summary>
    IEnumerable<AtomEntry> SavedAtoms(Timeline timeline);
}

/// <summary>
/// What a store holds for an atom.
/// </summary>
/// <param name="Value">The newest value written. Null for an atom the store doesn't hold, like an unset one.</param>
/// <param name="LastUpdate">The event that last wrote the atom.</param>
public readonly record struct AtomEntry(Atom Value, SnowportId LastUpdate);

/// <summary>
/// The store of <typeparamref name="T"/>, read without knowing the type of its ids.
/// </summary>
public interface IAtomStore<T> : IAtomStore
    where T : Atom
{
    /// <summary>
    /// The atoms with <paramref name="key"/> in <paramref name="lookup"/>, leaving out unset ones.
    /// The lookup is indexed on first read and kept up to date from then on.
    /// </summary>
    List<T> Get<K>(Lookup<T, K> lookup, K key);

    /// <summary>How many atoms with <paramref name="key"/> in <paramref name="lookup"/> aren't unset.</summary>
    int Count<K>(Lookup<T, K> lookup, K key);
}

/// <summary>
/// The store of <typeparamref name="T"/>, whose ids are <typeparamref name="I"/>.
/// </summary>
public interface IAtomStore<T, I> : IAtomStore<T>
    where T : Atom
{
    /// <summary>
    /// The atom with <paramref name="id"/>, or an entry with a null value if this store doesn't hold it.
    /// </summary>
    AtomEntry Find(I id);
}

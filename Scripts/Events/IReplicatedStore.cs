using System;
using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Holds the current records of one type, worked out from the event log.
/// <see cref="RecordService"/> makes one for each type the first time it's read or written.
/// </summary>
public interface IReplicatedStore
{
    /// <summary>The type of record held, unique across stores.</summary>
    Type RecordType { get; }

    /// <summary>Raised with the before and after of every record that changed.</summary>
    event Action<IReadOnlyList<RecordChange>> Changed;

    /// <summary>
    /// Merges an event that was just recorded in <paramref name="log"/>.
    /// <paramref name="changed"/> are the events whose records it puts in or out of effect:
    /// the event itself, or what an undo or redo reverses.
    /// While <paramref name="bulkLoading"/>, the changes wait for <see cref="FlushBulkLoad"/>.
    /// </summary>
    void Apply(EventLog log, IReadOnlyList<TableEvent> changed, bool bulkLoading);

    /// <summary>Removes everything, e.g. when the project is replaced, reporting each removal.</summary>
    void Clear();

    /// <summary>Sends one notification for everything merged during a bulk load.</summary>
    void FlushBulkLoad();

    /// <summary>
    /// The queries and values whose results <paramref name="changes"/> touched:
    /// each atom's id, and its group keys before and after in every group read so far.
    /// </summary>
    IEnumerable<Touch> Touched(IReadOnlyList<RecordChange> changes);

    /// <summary>The record with <paramref name="id"/> even if it is deleted, or null if this store doesn't hold it.</summary>
    Replicated Find(SnowTag id);

    /// <summary>The current value of every record that's saved with the project.</summary>
    IEnumerable<Replicated> SavedRecords();
}

/// <summary>
/// The store of <typeparamref name="T"/>, read without knowing the type of its ids.
/// </summary>
public interface IAtomStore<T> : IReplicatedStore
    where T : Replicated
{
    /// <summary>
    /// The atoms with <paramref name="key"/> in <paramref name="group"/>, deleted ones included.
    /// The group is indexed on first read and kept up to date from then on.
    /// </summary>
    IReadOnlyCollection<T> InGroup<G>(Group<T, G> group, G key);

    /// <summary>How many atoms with <paramref name="key"/> in <paramref name="group"/> aren't deleted.</summary>
    int Count<G>(Group<T, G> group, G key);
}

using System;
using System.Collections.Generic;
using Log = System.Collections.Generic.OrderedDictionary<SnowportId, TableEvent>;

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
    void Apply(Log log, IReadOnlyList<TableEvent> changed, bool bulkLoading);

    /// <summary>Removes everything, e.g. when the project is replaced, reporting each removal.</summary>
    void Clear();

    /// <summary>Sends one notification for everything merged during a bulk load.</summary>
    void FlushBulkLoad();

    /// <summary>The record with <paramref name="id"/> even if it is deleted, or null if this store doesn't hold it.</summary>
    Replicated Find(SnowTag id);

    /// <summary>The current value of every record that's saved with the project.</summary>
    IEnumerable<Replicated> SavedRecords();
}

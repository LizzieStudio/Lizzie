using System;
using System.Collections.Generic;

/// <summary>
/// Replicated project state that attaches to the EventSynchronizer.
/// </summary>
public interface IReplicatedContainer
{
    /// <summary>The type of record held, unique across containers.</summary>
    Type RecordType { get; }

    /// <summary>Raised with the before and after of every record that changed.</summary>
    event Action<IReadOnlyList<RecordChange>> Changed;

    void Attach(EventSynchronizer synchronizer);
    void Detach();
    void Clear();
    void FlushBulkLoad();

    /// <summary>The record with <paramref name="id"/> even if it is deleted, or null if this container doesn't hold it.</summary>
    IReplicated Find(SnowTag id);

    /// <summary>The effects that would recreate this container's current state.</summary>
    IEnumerable<Effect> EnumerateSaveEffects();
}

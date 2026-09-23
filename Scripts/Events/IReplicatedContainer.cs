using System.Collections.Generic;

/// <summary>
/// Replicated project state that attaches to the EventSynchronizer.
/// </summary>
public interface IReplicatedContainer
{
    void Attach(EventSynchronizer synchronizer);
    void Detach();
    void Clear();
    void FlushBulkLoad();

    /// <summary>The effects that would recreate this container's current state.</summary>
    IEnumerable<Effect> EnumerateSaveEffects();
}

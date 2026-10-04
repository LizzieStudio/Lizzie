using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// What only the machinery calls on <see cref="RecordService"/>.
/// It's implemented explicitly, so it doesn't show on <see cref="RecordService"/>;
/// reach it with <see cref="RecordServiceMachinery.Machinery"/>.
/// </summary>
public interface IRecordServiceMachinery
{
    /// <summary>
    /// Removes every record, e.g. when the project is replaced.
    /// </summary>
    void Clear();

    /// <summary>
    /// Sends each store's one notification for everything it merged during a bulk load.
    /// </summary>
    void FlushBulkLoad();

    /// <summary>
    /// Reruns the watcher at the end of the frame, if it's still watching.
    /// </summary>
    void MarkDirty(Watcher watcher);

    /// <summary>
    /// The current value of everything saved with the project.
    /// </summary>
    IEnumerable<Replicated> SavedRecords();
}

public static class RecordServiceMachinery
{
    /// <summary>
    /// The parts of <paramref name="records"/> only the machinery calls.
    /// </summary>
    public static IRecordServiceMachinery Machinery(this RecordService records) => records;
}

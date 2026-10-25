using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// What only the machinery calls on <see cref="Atoms"/>.
/// It's implemented explicitly, so it doesn't show on <see cref="Atoms"/>;
/// reach it with <see cref="AtomsMachinery.Machinery"/>.
/// </summary>
public interface IAtomsMachinery
{
    /// <summary>
    /// Removes every atom, e.g. when the project is replaced.
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
    /// Every atom saved with the project.
    /// </summary>
    IEnumerable<Atom> SavedAtoms();
}

public static class AtomsMachinery
{
    /// <summary>
    /// The parts of <paramref name="atoms"/> only the machinery calls.
    /// </summary>
    public static IAtomsMachinery Machinery(this Atoms atoms) => atoms;
}

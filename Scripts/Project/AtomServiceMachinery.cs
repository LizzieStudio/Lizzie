using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// What only the machinery calls on <see cref="AtomService"/>.
/// It's implemented explicitly, so it doesn't show on <see cref="AtomService"/>;
/// reach it with <see cref="AtomServiceMachinery.Machinery"/>.
/// </summary>
public interface IAtomServiceMachinery
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

public static class AtomServiceMachinery
{
    /// <summary>
    /// The parts of <paramref name="atoms"/> only the machinery calls.
    /// </summary>
    public static IAtomServiceMachinery Machinery(this AtomService atoms) => atoms;
}

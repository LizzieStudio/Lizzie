using System.Collections;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// A <see cref="WeakMap{TKey, TValue}"/> for code that doesn't know its types, like a save looking for SnowTags.
/// </summary>
public interface IWeakMap
{
    IDictionary Entries { get; }
}

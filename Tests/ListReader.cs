using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie.Tests;

/// <summary>Reads from a fixed list of atoms.</summary>
public sealed class ListReader(params Atom[] atoms) : IAtomReader
{
    public Atom Get(AtomRef atomRef) => throw new NotSupportedException();

    public SnowportId Version(Atom atom) => SnowportId.Empty;

    public T Get<T, I>(Identity<T, I> byId, I id)
        where T : Atom => atoms.OfType<T>().FirstOrDefault(a => byId.Of(a).Equals(id));

    public IReadOnlyList<T> Get<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom => atoms.OfType<T>().Where(a => Equals(lookup.Of(a), key)).ToList();

    public int Count<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom => Get(lookup, key).Count;
}

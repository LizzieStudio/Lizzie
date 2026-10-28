using System;
using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

namespace Lizzie.Tests;

/// <summary>Reads from a fixed list of atoms.</summary>
public sealed class ListReader(params Atom[] atoms) : IAtomReader
{
    public Atom Get(IAtomRef atomRef) => throw new NotSupportedException();

    public SnowportId Version(Atom atom) => SnowportId.Empty;

    public T Get<T, I>(Identity<T, I> byId, I id)
        where T : Atom
        where I : IAtomRef<T>, IComparable<I> =>
        atoms.OfType<T>().FirstOrDefault(a => byId.Of(a).Equals(id));

    public List<T> Get<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom => lookup.Ordered(atoms.OfType<T>().Where(a => Equals(lookup.Of(a), key)));

    public int Count<T, K>(Lookup<T, K> lookup, K key)
        where T : Atom => Get(lookup, key).Count;
}

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Reads from a fixed list of atoms.</summary>
public sealed class ListReader(params Atom[] atoms) : IAtomReader
{
    public Atom Get(AtomRef atomRef) => throw new NotSupportedException();

    public SnowportId Version(Atom atom) => SnowportId.Empty;

    public T Get<T, I>(Id<T, I> byId, I id)
        where T : Atom
        where I : IComparable<I> => atoms.OfType<T>().FirstOrDefault(a => byId.Of(a).Equals(id));

    public IReadOnlyList<T> Get<T, G>(Group<T, G> group, G key)
        where T : Atom => atoms.OfType<T>().Where(a => Equals(group.Of(a), key)).ToList();

    public int Count<T, G>(Group<T, G> group, G key)
        where T : Atom => Get(group, key).Count;
}

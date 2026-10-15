using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// A way to get atoms, declared as a static field on the atom's type.
///
/// <para>
/// Currently, there are only two types of <see cref="Query"/>: <see cref="Id{T, I}"/> and <see cref="Group{T, G}"/>.
/// A <see cref="RecordService.Watch"/> action that reads a <see cref="Query"/> will then update if the results of that <see cref="Query"/> change.
/// The <see cref="Query"/>s are also used to determine which records survive a save. An atom is kept if it is accessible from a chain of <see cref="Query"/>s.
/// </para>
/// </summary>
/// <remarks>
/// In general, we will not have to implement new types of <see cref="Query"/>.
/// </remarks>
public abstract class Query
{
    private protected Query() { }
}

/// <summary>
/// What makes two writes the same atom. Every atom type declares exactly one.
///
/// <para>
/// Atoms are immutable and are "updated" by writing a new one that overwrites the old one.
/// An atom's id determines which new writes replace an existing atom.
/// </para>
/// </summary>
public abstract class AtomId : Query
{
    private protected AtomId() { }

    /// <summary>
    /// The type of the ids.
    /// </summary>
    internal abstract Type IdType { get; }

    private protected abstract Type AtomType { get; }

    /// <summary>
    /// The id of <paramref name="atom"/>, boxed.
    /// </summary>
    internal abstract object Of(Replicated atom);

    private static readonly Dictionary<Type, AtomId> Declared = new();

    /// <summary>
    /// The Id that <paramref name="atomType"/> declares.
    /// Throws if it doesn't declare exactly one.
    /// </summary>
    public static AtomId For(Type atomType)
    {
        if (!Declared.TryGetValue(atomType, out var id))
            Declared[atomType] = id = Find(atomType);
        return id;
    }

    private static AtomId Find(Type atomType)
    {
        var ids = atomType
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => typeof(AtomId).IsAssignableFrom(f.FieldType))
            .Select(f => (AtomId)f.GetValue(null))
            .ToList();
        if (ids.Count != 1 || ids[0] == null || ids[0].AtomType != atomType)
            throw new InvalidOperationException(
                $"{atomType.Name} needs exactly one Id, declared as a public static readonly Id<{atomType.Name}, I> field, "
                    + "which says what makes two writes the same atom, like `public static readonly Id<DataRow, SnowTag> ById = new(r => r.Id);`."
            );
        return ids[0];
    }

    /// <summary>
    /// Uses reflection to get every atom type's Id whose ids are <see cref="SnowTag"/>s,
    /// for reading an atom whose type isn't known.
    /// </summary>
    internal static IReadOnlyList<AtomId> BySnowTag =>
        field ??= typeof(Replicated)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Replicated)) && !t.IsAbstract)
            .Select(For)
            .Where(id => id.IdType == typeof(SnowTag))
            .ToList();
}

/// <summary>
/// Every atom type <typeparamref name="T"/> must declare exactly one Id, named ById.
///
/// <para>
/// Writes of <typeparamref name="T"/> that produce the same id are considered to be the same atom.
/// A new write with the same id replaces the old, and deletions target an id.
/// </para>
/// <code>public static readonly Id&lt;DataRow, SnowTag&gt; ById = new(r => r.Id);</code>
/// Read an atom with <see cref="IRecordReader.Get{T, I}(Id{T, I}, I)"/>.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
/// <typeparam name="I">The type of the id.</typeparam>
public sealed class Id<T, I> : AtomId
    where T : Replicated
{
    private readonly Func<T, I> _of;

    /// <param name="of">
    /// The id of an atom.
    /// </param>
    public Id(Func<T, I> of) => _of = of;

    public I Of(T atom) => _of(atom);

    internal override object Of(Replicated atom) => _of((T)atom);

    internal override Type IdType => typeof(I);

    private protected override Type AtomType => typeof(T);
}

/// <summary>
/// A way to list atoms of <typeparamref name="T"/> by a key of <typeparamref name="G"/>.
///
/// <code>public static readonly Group&lt;DataRow, SnowTag&gt; ByDataSet = new(r => r.DataSetId);</code>
///
/// You could read from one of the keys of <c>ByDataSet</c> with
///
/// <code>R.Get(DataRow.ByDataSet, dataSetId)</code>
///
/// You may declare multiple Groups on one atom type.
/// Returning null for a group key keeps the atom out of any grouping.
/// Read a group with <see cref="IRecordReader.Get{T, G}(Group{T, G}, G)"/>.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
/// <typeparam name="G">The type of the group key.</typeparam>
public class Group<T, G> : Query
    where T : Replicated
{
    private readonly Func<T, G> _of;

    /// <param name="of">
    /// The grouping an atom is in, or null for none. It may only read the atom.
    /// </param>
    public Group(Func<T, G> of) => _of = of;

    /// <summary>
    /// The grouping <paramref name="atom"/> is in, or null for none.
    /// </summary>
    public G Of(T atom) => _of(atom);

    /// <summary>
    /// Compares group keys.
    /// </summary>
    internal IEqualityComparer<G> Comparer => EqualityComparer<G>.Default;
}

/// <summary>
/// Lists every atom of <typeparamref name="T"/>, read with <see cref="RecordReaderExtensions.Get{T}(IRecordReader, All{T})"/>.
/// A type without this cannot be enumerated.
/// You generally want to avoid adding this to most atom types, however, since save files will always keep an atom with an <see cref="All{T}"/>.
/// </summary>
public sealed class All<T>() : Group<T, bool>(_ => true)
    where T : Replicated;

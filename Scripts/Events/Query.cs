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

    /// <summary>
    /// The id or group key of <paramref name="atom"/>, boxed. Null for a group it isn't in.
    /// </summary>
    internal abstract object Of(Replicated atom);

    /// <summary>
    /// The queries <paramref name="atomType"/> declares as public static fields: its Id and its Groups.
    /// </summary>
    internal static IReadOnlyList<Query> DeclaredOn(Type atomType)
    {
        if (!Declared.TryGetValue(atomType, out var queries))
            Declared[atomType] = queries = atomType
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Query).IsAssignableFrom(f.FieldType))
                .Select(f => (Query)f.GetValue(null))
                .ToArray();
        return queries;
    }

    private static readonly Dictionary<Type, Query[]> Declared = new();
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
    /// Orders two atoms of this Id's type by their ids.
    /// </summary>
    internal abstract int Compare(Replicated a, Replicated b);

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
        var ids = DeclaredOn(atomType).OfType<AtomId>().ToList();
        if (ids.Count != 1 || ids[0] == null || ids[0].AtomType != atomType)
            throw new InvalidOperationException(
                $"{atomType.Name} needs exactly one Id, declared as a public static readonly Id<{atomType.Name}, I> field, "
                    + "which says what makes two writes the same atom, like `public static readonly Id<DataRow, SnowTag> ById = new(r => r.Id);`, "
                    + $"or as `public static readonly Singleton<{atomType.Name}> Only = new();` if it has only one atom."
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
/// Every atom type <typeparamref name="T"/> must declare exactly one Id,
/// unless it's a <see cref="Singleton{T}"/>.
///
/// <para>
/// Writes of <typeparamref name="T"/> that produce the same id are considered to be the same atom.
/// A new write with the same id replaces the old.
/// </para>
/// <code>public static readonly Id&lt;DataRow, SnowTag&gt; ById = new(r => r.Id);</code>
/// Read an atom with <see cref="IRecordReader.Get{T, I}(Id{T, I}, I)"/>.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
/// <typeparam name="I">The type of the id, which orders atoms in a save.</typeparam>
public class Id<T, I> : AtomId
    where T : Replicated
    where I : IComparable<I>
{
    private readonly Func<T, I> _of;

    /// <param name="of">
    /// The id of an atom.
    /// </param>
    public Id(Func<T, I> of) => _of = of;

    public I Of(T atom) => _of(atom);

    internal override object Of(Replicated atom) => _of((T)atom);

    internal override Type IdType => typeof(I);

    internal override int Compare(Replicated a, Replicated b) =>
        Comparer<I>.Default.Compare(_of((T)a), _of((T)b));

    private protected override Type AtomType => typeof(T);
}

/// <summary>
/// Declares a type with only one atom, like the project settings.
/// An atom with <see cref="Singleton{T}"/> does not need an <see cref="Id{T, I}"/>.
///
/// <code>public static readonly Singleton&lt;Seating&gt; Only = new();</code>
///
/// Read the atom with <see cref="RecordReaderExtensions.Get{T}(IRecordReader, Singleton{T})"/>,
/// which gives the default <typeparamref name="T"/> until the first write.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
public sealed class Singleton<T>() : Id<T, SingletonId>(_ => default)
    where T : Replicated, new()
{
    /// <summary>What reads give before the first write.</summary>
    internal T Unwritten => field ??= new();
}

/// <summary>
/// The id every write of a singleton shares. See <see cref="Singleton{T}"/>.
/// </summary>
public readonly record struct SingletonId : IComparable<SingletonId>
{
    public int CompareTo(SingletonId other) => 0;
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

    internal override object Of(Replicated atom) => _of((T)atom);
}

/// <summary>
/// Lists every atom of <typeparamref name="T"/>, read with <see cref="RecordReaderExtensions.Get{T}(IRecordReader, All{T})"/>.
/// A type without this cannot be enumerated.
/// You generally want to avoid adding this to most atom types, however, since save files will always keep an atom an <see cref="All{T}"/> lists.
/// </summary>
/// <param name="where">Which atoms it lists, or every one when null.</param>
public sealed class All<T>(Func<T, bool> where = null)
    : Group<T, bool?>(atom => where == null || where(atom) ? true : null)
    where T : Replicated;

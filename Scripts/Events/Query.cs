using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Lizzie;

/// <summary>
/// A way to get atoms, declared as a static field on the atom's type.
///
/// <para>
/// Currently, there are only two types of <see cref="Query"/>: <see cref="Identity{T, I}"/> and <see cref="Lookup{T, K}"/>.
/// A <see cref="Atoms.Watch"/> action that reads a <see cref="Query"/> will then update if the results of that <see cref="Query"/> change.
/// The <see cref="Query"/>s are also used to determine which atoms survive a save. An atom is kept if it is accessible from a chain of <see cref="Query"/>s.
/// </para>
/// </summary>
/// <remarks>
/// In general, we will not have to implement new types of <see cref="Query"/>.
/// </remarks>
public abstract class Query
{
    private protected Query() { }

    /// <summary>
    /// The id or lookup key of <paramref name="atom"/>, boxed. Null for a lookup it isn't in.
    /// </summary>
    internal abstract object Of(Atom atom);

    /// <summary>
    /// The queries <paramref name="atomType"/> declares as public static fields: its Id and its Lookups.
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
public abstract class IdQuery : Query
{
    private protected IdQuery() { }

    /// <summary>
    /// The type of the ids.
    /// </summary>
    internal abstract Type IdType { get; }

    private protected abstract Type AtomType { get; }

    /// <summary>
    /// What stands in for the atom with <paramref name="id"/> before it exists, or null if the type has no stand-in.
    /// </summary>
    internal abstract Atom Unwritten(object id);

    private static readonly Dictionary<Type, IdQuery> Declared = new();

    /// <summary>
    /// The Id that <paramref name="atomType"/> declares.
    /// Throws if it doesn't declare exactly one.
    /// </summary>
    public static IdQuery For(Type atomType)
    {
        if (!Declared.TryGetValue(atomType, out var id))
            Declared[atomType] = id = Find(atomType);
        return id;
    }

    private static IdQuery Find(Type atomType)
    {
        var ids = DeclaredOn(atomType).OfType<IdQuery>().ToList();
        if (ids.Count != 1 || ids[0] == null || ids[0].AtomType != atomType)
            throw new InvalidOperationException(
                $"{atomType.Name} needs exactly one Id, declared as a public static readonly Identity<{atomType.Name}, I> field, "
                    + $"which says what makes two writes the same atom, like `public static readonly Identity<{atomType.Name}, Ref<{atomType.Name}>> ById = new(r => r.Id);`, "
                    + $"or as `public static readonly Singleton<{atomType.Name}> Only = new();` if it has only one atom."
            );
        if (typeof(StagedAtom).IsAssignableFrom(atomType) != ids[0] is IStagedId)
            throw new InvalidOperationException(
                $"{atomType.Name} must declare a StagedId when it's a StagedAtom, "
                    + $"like `public static readonly StagedId<{atomType.Name}> ById = new(c => c.Id);`."
            );
        return ids[0];
    }
}

/// <summary>
/// Every atom type <typeparamref name="T"/> must declare exactly one Id,
/// unless it's a <see cref="Singleton{T}"/>.
///
/// <para>
/// Writes of <typeparamref name="T"/> that produce the same id are considered to be the same atom.
/// A new write with the same id replaces the old.
/// </para>
/// <code>public static readonly Identity&lt;DataRow, Ref&lt;DataRow&gt;&gt; ById = new(r => r.Id);</code>
/// Read an atom with <see cref="IAtomReader.Get{T, I}(Identity{T, I}, I)"/>.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
/// <typeparam name="I">The type of the id.</typeparam>
public class Identity<T, I> : IdQuery
    where T : Atom
    where I : IAtomRef<T>
{
    private readonly Func<T, I> _of;
    private readonly Func<I, T> _unwritten;

    /// <param name="of">
    /// The id of an atom.
    /// </param>
    /// <param name="unwritten">
    /// What a command or a relation acts on for an atom that doesn't exist.
    /// Reads still give null for it.
    /// </param>
    public Identity(Func<T, I> of, Func<I, T> unwritten = null)
    {
        _of = of;
        _unwritten = unwritten;
    }

    public I Of(T atom) => _of(atom);

    internal override object Of(Atom atom) => _of((T)atom);

    /// <summary>
    /// What stands in for the atom with <paramref name="id"/> before it exists.
    /// </summary>
    internal virtual T Unwritten(I id) => _unwritten?.Invoke(id);

    internal override Atom Unwritten(object id) => Unwritten((I)id);

    internal override Type IdType => typeof(I);

    private protected override Type AtomType => typeof(T);
}

/// <summary>
/// Declares a type with only one atom, like the project settings.
/// An atom with <see cref="Singleton{T}"/> does not need an <see cref="Identity{T, I}"/>.
///
/// <code>public static readonly Singleton&lt;Seating&gt; Only = new();</code>
///
/// Read the atom with <see cref="AtomReaderExtensions.Get{T}(IAtomReader, Singleton{T})"/>,
/// which gives the default <typeparamref name="T"/> until the first write.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
public sealed class Singleton<T>() : Identity<T, SingletonId<T>>(_ => default)
    where T : Atom, new()
{
    // What reads give before the first write, made on the first read.
    private T Shared => field ??= new();

    internal override T Unwritten(SingletonId<T> id) => Shared;
}

/// <summary>
/// The id every write of a singleton shares. See <see cref="Singleton{T}"/>.
/// </summary>
public readonly record struct SingletonId<T> : IAtomRef<T>
    where T : Atom;

/// <summary>
/// A way to list atoms of <typeparamref name="T"/> by a key of <typeparamref name="K"/>.
///
/// <code>public static readonly Lookup&lt;DataRow, Ref&lt;Dataset&gt;&gt; ByDataset = new(r => r.DatasetRef);</code>
///
/// You could read from one of the keys of <c>ByDataset</c> with
///
/// <code>R.Get(DataRow.ByDataset, datasetId)</code>
///
/// You may declare multiple Lookups on one atom type.
/// Returning null for a key keeps the atom out of the lookup.
/// Read a lookup with <see cref="IAtomReader.Get{T, K}(Lookup{T, K}, K)"/>.
/// </summary>
/// <typeparam name="T">The type of the atom.</typeparam>
/// <typeparam name="K">The type of the key.</typeparam>
public class Lookup<T, K> : Query
    where T : Atom
{
    private readonly Func<T, K> _of;

    /// <param name="of">
    /// The key an atom is under, or null for none. It may only read the atom.
    /// </param>
    public Lookup(Func<T, K> of) => _of = of;

    /// <summary>
    /// The key <paramref name="atom"/> is under, or null for none.
    /// </summary>
    public K Of(T atom) => _of(atom);

    internal override object Of(Atom atom) => _of((T)atom);
}

/// <summary>
/// The Id of a <see cref="StagedAtom"/> type.
///
/// <code>public static readonly StagedId&lt;ComponentPosition&gt; ById = new(c => c.Id);</code>
/// </summary>
public sealed class StagedId<T>(Func<T, Ref<Component>> of)
    : Identity<T, StageKey<T>>(atom => new(atom.StageRef, of(atom))),
        IStagedId
    where T : StagedAtom;

/// <summary>
/// Marks a <see cref="StagedId{T}"/> for code that doesn't know <c>T</c>.
/// </summary>
internal interface IStagedId;

/// <summary>
/// For a staged atom type, lists its updates in each stage.
///
/// <code>public static readonly Written&lt;ComponentPosition&gt; WrittenIn = new();</code>
/// </summary>
/// <remarks>
/// This is used internally to differentiate between a component that was inherited from a blueprint
/// and a component that was actually updated in a stage.
/// </remarks>
public sealed class Written<T>() : Lookup<T, Ref<Stage>>(atom => atom.StageRef)
    where T : StagedAtom;

/// <summary>
/// Lists every atom of <typeparamref name="T"/>, read with <see cref="AtomReaderExtensions.Get{T}(IAtomReader, All{T})"/>.
/// A type without this cannot be enumerated.
/// You generally want to avoid adding this to most atom types, however, since save files will always keep an atom an <see cref="All{T}"/> lists.
/// </summary>
/// <param name="where">Which atoms it lists, or every one when null.</param>
public sealed class All<T>(Func<T, bool> where = null)
    : Lookup<T, bool?>(atom => where == null || where(atom) ? true : null)
    where T : Atom;

using System;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// An atom that each <see cref="Stage"/> holds its own version of, like a component's state.
///
/// <para>
/// The state of a <see cref="StagedAtom"/> is temporal.
/// If you change it in a blueprint, it also changes in the blueprints and tables built on it.
/// This is different from a normal <see cref="Atom"/> like a prototype, which changes everywhere.
/// </para>
///
/// <para>
/// When you read a staged atom from a stage, you get the most recent update to that atom, up to that stage.
/// The atom you get, however, shows the stage you read it from, meaning you can write it immediately using <c>with { ... }</c>.
/// </para>
/// </summary>
/// <remarks>
/// A staged atom type declares its Id as a <see cref="StagedId{T}"/>, and gets a <see cref="StagedStore{T}"/>.
/// Any <see cref="Lookup{T, K}"/> lists what each stage shows, and any <see cref="Written{T}"/> lists what each stage wrote.
/// </remarks>
public abstract record StagedAtom : Atom
{
    /// <summary>
    /// The stage this atom was written in or read from.
    /// </summary>
    public Ref<Stage> StageRef { get; init; }
}

/// <summary>
/// The id of a <see cref="StagedAtom"/> of <typeparamref name="T"/>.
/// </summary>
public readonly record struct StageKey<T>(Ref<Stage> StageRef, Ref<Component> ComponentRef)
    : IAtomRef<T>,
        IComparable<StageKey<T>>
    where T : StagedAtom
{
    public int CompareTo(StageKey<T> other) =>
        StageRef.CompareTo(other.StageRef) is var c and not 0
            ? c
            : ComponentRef.CompareTo(other.ComponentRef);
}

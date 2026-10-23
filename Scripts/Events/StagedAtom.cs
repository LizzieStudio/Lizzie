using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// An atom that each <see cref="Lizzie.Stage"/> holds its own version of, like a component's state.
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
    public SnowTag Stage { get; init; }

    /// <summary>
    /// A tombstone. When true, this stage hides the atom the previous stages show.
    /// </summary>
    /// <remarks>
    /// This is a deletion. As in, this atom shouldn't exist in this stage anymore.
    /// To reset a staged atom to the parent stage, use <see cref="Unset"/>.
    /// </remarks>
    public bool Removed { get; init; }
}

/// <summary>
/// The id of a <see cref="StagedAtom"/>.
/// </summary>
public readonly record struct StageKey(SnowTag Stage, SnowTag Component);

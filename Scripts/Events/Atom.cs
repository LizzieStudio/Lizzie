using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// <para>An atom, easily replicated in multiplayer through a write-only synchronized timeline.</para>
/// <para>
/// Write atoms with <see cref="Atoms.Write(Node, IEnumerable{IChange})"/>,
/// or return them from a command's <see cref="AtomCommand{T}.Effects"/>.
/// Unset them by writing <see cref="AtomReaderExtensions.Unset"/>.
/// </para>
/// </summary>
/// <remarks>
/// Every atom type needs a <see cref="JsonNameAttribute"/>, the name it's saved and sent under,
/// and an <see cref="Identity{T, I}"/> named ById, or a <see cref="Singleton{T}"/> named Only.
/// </remarks>
[JsonPolymorphic]
public abstract record Atom : IChange
{
    /// <summary>
    /// What's directly inside this atom, like a deck's cards or a DataRow's cells.
    /// These are followed recursively, so only list direct contents.
    /// </summary>
    public virtual IEnumerable<IAtomRef> Contents(IAtomReader R) => [];

    /// <summary>
    /// What this atom is directly inside, like a card's deck or a cell's <see cref="DataRow"/> and <see cref="DataColumn"/>.
    /// These are followed recursively, so only list direct containers.
    /// </summary>
    public virtual IEnumerable<IAtomRef> Containers(IAtomReader R) => [];

    /// <summary>
    /// What this atom refers to, like a component's prototype.
    /// Not followed further.
    /// </summary>
    public virtual IEnumerable<IAtomRef> Referenced(IAtomReader R) => [];

    /// <summary>
    /// The Transition this value is a draft of, or <see cref="Transition.None"/>.
    /// </summary>
    public Transition Transition { get; init; }
}

/// <summary>
/// Keeps this atom out of saved projects.
/// This atom will be used in runtime multiplayer only.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotSavedAttribute : Attribute;

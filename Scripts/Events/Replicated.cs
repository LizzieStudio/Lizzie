using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// <para>A record, easily replicated in multiplayer through a write-only synchronized log.</para>
/// <para>
/// Write records with <see cref="RecordService.Write(IEnumerable{IChange})"/>,
/// or return them from a command's <see cref="Command.Effects"/>.
/// Unset them by writing <see cref="RecordReaderExtensions.Unset"/>.
/// </para>
/// </summary>
/// <remarks>
/// Every record type needs a <see cref="JsonNameAttribute"/>, the name it's saved and sent under,
/// and an <see cref="Id{T, I}"/> named ById, or a <see cref="Singleton{T}"/> named Only.
/// </remarks>
[JsonPolymorphic]
public abstract record Replicated : IChange
{
    /// <summary>
    /// What's directly inside this record, like a deck's cards or a DataRow's cells.
    /// These are followed recursively, so only list direct contents.
    /// </summary>
    public virtual IEnumerable<AtomRef> Contents(IRecordReader R) => [];

    /// <summary>
    /// What this record is directly inside, like a card's deck or a cell's <see cref="DataRow"/> and <see cref="DataColumn"/>.
    /// These are followed recursively, so only list direct containers.
    /// </summary>
    public virtual IEnumerable<AtomRef> Containers(IRecordReader R) => [];

    /// <summary>
    /// What this record refers to, like a component's prototype.
    /// Not followed further.
    /// </summary>
    public virtual IEnumerable<AtomRef> Referenced(IRecordReader R) => [];
}

/// <summary>
/// Keeps this record out of saved projects.
/// This record will be used in runtime multiplayer only.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotSavedAttribute : Attribute;

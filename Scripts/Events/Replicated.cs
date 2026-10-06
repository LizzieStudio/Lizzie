using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// <para>A record, easily replicated in multiplayer through a write-only synchronized log.</para>
/// <para>
/// Write records with <see cref="RecordService.Write(IEnumerable{Replicated})"/>,
/// or return them from a command's <see cref="Command.Effects"/>.
/// </para>
/// </summary>
/// <remarks>
/// Every record type needs a <see cref="JsonNameAttribute"/>, the name it's saved and sent under.
/// </remarks>
[JsonPolymorphic]
public abstract record Replicated
{
    /// <summary>
    /// The record's identity, usually created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// Reversible soft-delete flag.
    /// Will be permanently deleted in save files unless a kept record references it.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// The id of the event that last wrote this record.
    /// </summary>
    [JsonIgnore]
    public SnowportId LastUpdateId { get; init; }

    /// <summary>
    /// A cache key that changes whenever the record is updated.
    /// </summary>
    public string SheetKey() => $"{Id.Value:X8}{LastUpdateId.Value:X16}";

    /// <summary>
    /// What's directly inside this record, like a deck's cards or a DataRow's cells.
    /// These are followed recursively, so only list direct contents.
    /// </summary>
    public virtual IEnumerable<Target> Contents(IRecordReader R) => [];

    /// <summary>
    /// What this record is directly inside, like a card's deck or a cell's <see cref="DataRow"/> and <see cref="ColumnTarget"/>.
    /// These are followed recursively, so only list direct containers.
    /// </summary>
    public virtual IEnumerable<Target> Containers(IRecordReader R) => [];

    /// <summary>
    /// What this record refers to, like a component's prototype.
    /// Not followed further.
    /// </summary>
    public virtual IEnumerable<Target> Referenced(IRecordReader R) => [];
}

public static class ReplicatedExtensions
{
    /// <summary>
    /// A cache key that changes whenever any of the records is updated.
    /// </summary>
    public static string SheetKey(this IEnumerable<Replicated> items)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var item in items)
            sb.Append(item.SheetKey());
        return sb.ToString();
    }
}

/// <summary>
/// Keeps this record out of saved projects.
/// This record will be used in runtime multiplayer only.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotSavedAttribute : Attribute;

/// <summary>
/// <para>Exactly one record of this type exists, accessed with <see cref="IRecordReader.Single{T}"/>.</para>
/// <para>
/// The method <see cref="ProjectService.EnsureSingletons"/> will create this record during table setup, so it always exists.
/// </para>
/// </summary>
/// <remarks>
/// Important: The type needs a default constructor without parameters
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SingletonAttribute : Attribute;

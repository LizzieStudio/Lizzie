using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public abstract record Replicated : IReplicated
{
    public SnowTag Id { get; init; }

    public bool Deleted { get; init; }

    [JsonIgnore]
    public SnowportId LastUpdateId { get; init; }

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with
        {
            Id = id,
            LastUpdateId = lastUpdateId,
        };

    public string SheetKey() => $"{Id.Value:X8}{LastUpdateId.Value:X16}";

    public virtual IEnumerable<Target> Contents(IRecordReader R) => [];

    public virtual IEnumerable<Target> Containers(IRecordReader R) => [];

    public virtual IEnumerable<Target> Referenced(IRecordReader R) => [];
}

/// <summary>
/// Disables automatic saving in projects.
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

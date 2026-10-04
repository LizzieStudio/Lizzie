using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Something a player can select and run commands on.
/// </summary>
[JsonPolymorphic]
public abstract record Target
{
    /// <inheritdoc cref="Replicated.Contents"/>
    public virtual IEnumerable<Target> Contents(IRecordReader R) => [];

    /// <inheritdoc cref="Replicated.Containers"/>
    public virtual IEnumerable<Target> Containers(IRecordReader R) => [];

    /// <inheritdoc cref="Replicated.Referenced"/>
    public virtual IEnumerable<Target> Referenced(IRecordReader R) => [];
}

/// <summary>
/// Targets a record.
/// </summary>
[JsonName("RecordTarget")]
public sealed record RecordTarget(SnowTag Id) : Target
{
    public override IEnumerable<Target> Contents(IRecordReader R) => R.Get(Id)?.Contents(R) ?? [];

    public override IEnumerable<Target> Containers(IRecordReader R) =>
        R.Get(Id)?.Containers(R) ?? [];

    public override IEnumerable<Target> Referenced(IRecordReader R) =>
        R.Get(Id)?.Referenced(R) ?? [];
}

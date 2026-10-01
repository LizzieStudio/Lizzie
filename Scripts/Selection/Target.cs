using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Something a player can select and run commands on.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(RecordTarget), "r")]
[JsonDerivedType(typeof(ColumnTarget), "col")]
[JsonDerivedType(typeof(CellTarget), "cell")]
public abstract record Target
{
    /// <inheritdoc cref="IReplicated.Contents"/>
    public virtual IEnumerable<Target> Contents(IRecordReader R) => [];

    /// <inheritdoc cref="IReplicated.Containers"/>
    public virtual IEnumerable<Target> Containers(IRecordReader R) => [];

    /// <inheritdoc cref="IReplicated.Referenced"/>
    public virtual IEnumerable<Target> Referenced(IRecordReader R) => [];
}

/// <summary>
/// Targets a record.
/// </summary>
public sealed record RecordTarget([property: JsonPropertyName("i")] SnowTag Id) : Target
{
    public override IEnumerable<Target> Contents(IRecordReader R) => R.Get(Id)?.Contents(R) ?? [];

    public override IEnumerable<Target> Containers(IRecordReader R) =>
        R.Get(Id)?.Containers(R) ?? [];

    public override IEnumerable<Target> Referenced(IRecordReader R) =>
        R.Get(Id)?.Referenced(R) ?? [];
}

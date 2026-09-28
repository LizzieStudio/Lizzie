using System.Text.Json.Serialization;

/// <summary>
/// Something a player can select and run commands on.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "@")]
[JsonDerivedType(typeof(RecordTarget), "r")]
[JsonDerivedType(typeof(ColumnTarget), "col")]
[JsonDerivedType(typeof(CellTarget), "cell")]
public abstract record Target;

/// <summary>
/// A record such as a component, prototype, dataset, or row.
/// </summary>
public sealed record RecordTarget([property: JsonPropertyName("i")] SnowTag Id) : Target;

/// <summary>
/// A dataset column, which lives inside its dataset.
/// </summary>
public sealed record ColumnTarget(
    [property: JsonPropertyName("d")] SnowTag DataSetId,
    [property: JsonPropertyName("i")] SnowTag ColumnId
) : Target;

/// <summary>
/// One cell of a dataset row.
/// </summary>
public sealed record CellTarget(
    [property: JsonPropertyName("r")] SnowTag RowId,
    [property: JsonPropertyName("i")] SnowTag ColumnId
) : Target;

using System;
using System.Collections.Generic;
using System.Linq;

[JsonName("DataSet")]
public record DataSet : Replicated
{
    public static readonly Id<DataSet, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public static readonly All<DataSet> All = new();

    public string Name { get; init; }
}

/// <summary>
/// A dataset column.
/// </summary>
[JsonName("DataColumn")]
public record DataColumn : Replicated, IRanked
{
    public static readonly Id<DataColumn, SnowTag> ById = new(c => c.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// The columns of each dataset, in no order. <see cref="RecordReaderExtensions.GetColumns"/> sorts them.
    /// </summary>
    public static readonly Group<DataColumn, SnowTag> ByDataSet = new(c => c.DataSetId);

    /// <summary>
    /// The dataset this column belongs to.
    /// </summary>
    public SnowTag DataSetId { get; init; }

    /// <summary>
    /// The position of this column in its dataset.
    /// </summary>
    public SnowRank Rank { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// A dataset column.
/// </summary>
[JsonName("ColumnTarget")]
public sealed record ColumnTarget(SnowTag DataSetId, SnowTag ColumnId) : Target
{
    public override IEnumerable<Target> Contents(IRecordReader R) =>
        R.Get(DataColumn.ById, ColumnId) != null
            ? R.Get(DataRow.ByDataSet, DataSetId).Select(r => new CellTarget(r.Id, ColumnId))
            : [];

    public override IEnumerable<Target> Containers(IRecordReader R) =>
        [new RecordTarget(DataSetId)];
}

/// <summary>
/// A single dataset row.
/// </summary>
[JsonName("DataRow")]
public record DataRow : Replicated, IRanked
{
    public static readonly Id<DataRow, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Snowport.CreateTag"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// The rows of each dataset, in no order. <see cref="RecordReaderExtensions.GetRows"/> sorts them.
    /// </summary>
    public static readonly Group<DataRow, SnowTag> ByDataSet = new(r => r.DataSetId);

    /// <summary>
    /// The dataset this row belongs to.
    /// </summary>
    public SnowTag DataSetId { get; init; }

    /// <summary>
    /// The position of this row in its dataset.
    /// </summary>
    public SnowRank Rank { get; init; }

    /// <summary>
    /// A cell for each of the dataset's columns.
    /// </summary>
    public override IEnumerable<Target> Contents(IRecordReader R) =>
        R.GetColumns(DataSetId).Select(c => new CellTarget(Id, c.Id));

    /// <summary>
    /// A row is inside its dataset.
    /// </summary>
    public override IEnumerable<Target> Containers(IRecordReader R) =>
        [new RecordTarget(DataSetId)];
}

/// <summary>
/// The value of one cell of a dataset.
/// </summary>
[JsonName("DataCell")]
public record DataCell : Replicated
{
    public static readonly Id<DataCell, CellId> ById = new(c => c.Id);

    public required CellId Id { get; init; }

    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// The change that sets a cell to <paramref name="value"/>.
    /// </summary>
    public static IEnumerable<IChange> Set(IRecordReader R, CellId id, string value) =>
        value.Length > 0 ? [new DataCell { Id = id, Value = value }]
        : R.Get(ById, id) is { } old ? [R.Unset(old)]
        : [];
}

/// <summary>
/// The Id of a <see cref="DataCell"/>.
/// </summary>
public readonly record struct CellId(SnowTag Row, SnowTag Column) : IComparable<CellId>
{
    public int CompareTo(CellId other) =>
        Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);
}

/// <summary>
/// One cell of a dataset.
/// </summary>
[JsonName("CellTarget")]
public sealed record CellTarget(SnowTag RowId, SnowTag ColumnId) : Target
{
    public override IEnumerable<Target> Containers(IRecordReader R) =>
        R.Get(DataRow.ById, RowId) is { } row
            ? [new RecordTarget(row.Id), new ColumnTarget(row.DataSetId, ColumnId)]
            : [];
}

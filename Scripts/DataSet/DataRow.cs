using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// A single dataset row.
/// </summary>
public record DataRow : Replicated
{
    /// <summary>The dataset this row belongs to.</summary>
    public SnowTag DataSetId { get; init; }

    /// <summary>LexoRank-style ordering key. See <see cref="RowRank"/>.</summary>
    public string Rank { get; init; } = string.Empty;

    /// <summary>Cell values keyed by <see cref="Column.Id"/>.</summary>
    public ImmutableDictionary<SnowTag, string> Data { get; init; } =
        ImmutableDictionary<SnowTag, string>.Empty;

    /// <summary>A cell for each of the dataset's columns.</summary>
    public override IEnumerable<Target> Contents(IRecordReader R) =>
        R.Get<DataSet>(DataSetId)?.Columns.Select(c => new CellTarget(Id, c.Id)) ?? [];

    /// <summary>A row is inside its dataset.</summary>
    public override IEnumerable<Target> Containers(IRecordReader R) =>
        [new RecordTarget(DataSetId)];

    /// <summary>The row with a cell set to <paramref name="value"/>. An empty value removes the cell.</summary>
    public DataRow WithCell(SnowTag column, string value) =>
        this with
        {
            Data = value.Length == 0 ? Data.Remove(column) : Data.SetItem(column, value),
        };
}

/// <summary>
/// One cell of a dataset.
/// </summary>
public sealed record CellTarget(SnowTag RowId, SnowTag ColumnId) : Target
{
    public override IEnumerable<Target> Containers(IRecordReader R) =>
        R.Get<DataRow>(RowId) is { } row
            ? [new RecordTarget(row.Id), new ColumnTarget(row.DataSetId, ColumnId)]
            : [];
}

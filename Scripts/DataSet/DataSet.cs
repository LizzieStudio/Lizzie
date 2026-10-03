using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

public record DataSet : Replicated
{
    public string Name { get; init; }

    /// <summary>
    /// The dataset's columns, in display order.
    /// </summary>
    public ImmutableArray<Column> Columns { get; init; } = ImmutableArray<Column>.Empty;
}

public record Column
{
    public SnowTag Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// A dataset column.
/// </summary>
public sealed record ColumnTarget(SnowTag DataSetId, SnowTag ColumnId) : Target
{
    public override IEnumerable<Target> Contents(IRecordReader R) =>
        R.Get<DataSet>(DataSetId)?.Columns.Any(c => c.Id == ColumnId) == true
            ? R.Get<DataRow>(r => r.DataSetId == DataSetId)
                .Select(r => new CellTarget(r.Id, ColumnId))
            : [];

    public override IEnumerable<Target> Containers(IRecordReader R) =>
        [new RecordTarget(DataSetId)];
}

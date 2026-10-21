using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

[JsonName("DataSet")]
public record DataSet : Atom
{
    public static readonly Identity<DataSet, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public static readonly All<DataSet> All = new();

    public string Name { get; init; }
}

/// <summary>
/// A dataset column.
/// </summary>
[JsonName("DataColumn")]
public record DataColumn : Atom, IRanked
{
    public static readonly Identity<DataColumn, SnowTag> ById = new(c => c.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// The columns of each dataset, in no order. <see cref="AtomReaderExtensions.GetColumns"/> sorts them.
    /// </summary>
    public static readonly Lookup<DataColumn, SnowTag> ByDataSet = new(c => c.DataSetId);

    /// <summary>
    /// The dataset this column belongs to.
    /// </summary>
    public SnowTag DataSetId { get; init; }

    /// <summary>
    /// The position of this column in its dataset.
    /// </summary>
    public SnowRank Rank { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// A cell for each of the dataset's rows.
    /// </summary>
    public override IEnumerable<AtomRef> Contents(IAtomReader R) =>
        R.Get(DataRow.ByDataSet, DataSetId).Select(r => DataCell.ById.Ref(new CellId(r.Id, Id)));

    /// <summary>
    /// A column is inside its dataset.
    /// </summary>
    public override IEnumerable<AtomRef> Containers(IAtomReader R) => [DataSet.ById.Ref(DataSetId)];
}

/// <summary>
/// A single dataset row.
/// </summary>
[JsonName("DataRow")]
public record DataRow : Atom, IRanked
{
    public static readonly Identity<DataRow, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    /// <summary>
    /// The rows of each dataset, in no order. <see cref="AtomReaderExtensions.GetRows"/> sorts them.
    /// </summary>
    public static readonly Lookup<DataRow, SnowTag> ByDataSet = new(r => r.DataSetId);

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
    public override IEnumerable<AtomRef> Contents(IAtomReader R) =>
        R.GetColumns(DataSetId).Select(c => DataCell.ById.Ref(new CellId(Id, c.Id)));

    /// <summary>
    /// A row is inside its dataset.
    /// </summary>
    public override IEnumerable<AtomRef> Containers(IAtomReader R) => [DataSet.ById.Ref(DataSetId)];
}

/// <summary>
/// The value of one cell of a dataset.
/// </summary>
[JsonName("DataCell")]
public record DataCell : Atom
{
    /// <remarks>
    /// When you get a cell that doesn't exist, a stand-in is created.
    /// </remarks>
    public static readonly Identity<DataCell, CellId> ById = new(
        c => c.Id,
        unwritten: id => new DataCell { Id = id }
    );

    public required CellId Id { get; init; }

    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// The change that sets a cell to <paramref name="value"/>.
    /// </summary>
    public static IEnumerable<IChange> Set(IAtomReader R, CellId id, string value) =>
        value.Length > 0 ? [new DataCell { Id = id, Value = value }]
        : R.Get(ById, id) is { } old ? [R.Unset(old)]
        : [];

    /// <summary>
    /// A cell is inside its row and its column.
    /// </summary>
    public override IEnumerable<AtomRef> Containers(IAtomReader R) =>
        [DataRow.ById.Ref(Id.Row), DataColumn.ById.Ref(Id.Column)];
}

/// <summary>
/// The Id of a <see cref="DataCell"/>.
/// </summary>
public readonly record struct CellId(SnowTag Row, SnowTag Column);

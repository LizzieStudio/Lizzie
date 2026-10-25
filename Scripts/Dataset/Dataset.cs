using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

[JsonName("Dataset")]
public record Dataset : Atom
{
    public static readonly Identity<Dataset, SnowTag> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="SnowTag.Create"/>.
    /// </summary>
    public required SnowTag Id { get; init; }

    public static readonly All<Dataset> All = new();

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
    public static readonly Lookup<DataColumn, SnowTag> ByDataset = new(c => c.DatasetId);

    /// <summary>
    /// The dataset this column belongs to.
    /// </summary>
    public SnowTag DatasetId { get; init; }

    /// <summary>
    /// The position of this column in its dataset.
    /// </summary>
    public SnowRank Rank { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// A cell for each of the dataset's rows.
    /// </summary>
    public override IEnumerable<AtomRef> Contents(IAtomReader R) =>
        R.Get(DataRow.ByDataset, DatasetId).Select(r => DataCell.ById.Ref(new CellId(r.Id, Id)));

    /// <summary>
    /// A column is inside its dataset.
    /// </summary>
    public override IEnumerable<AtomRef> Containers(IAtomReader R) => [Dataset.ById.Ref(DatasetId)];
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
    /// The rows of each dataset that aren't deleted, in no order. <see cref="AtomReaderExtensions.GetRows"/> sorts them.
    /// </summary>
    public static readonly Lookup<DataRow, SnowTag?> ByDataset = new(r =>
        r.Deleted ? null : r.DatasetId
    );

    /// <summary>
    /// The dataset this row belongs to.
    /// </summary>
    public SnowTag DatasetId { get; init; }

    /// <summary>
    /// The position of this row in its dataset.
    /// </summary>
    public SnowRank Rank { get; init; }

    /// <summary>
    /// Set when the player deletes the row.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// A cell for each of the dataset's columns.
    /// </summary>
    public override IEnumerable<AtomRef> Contents(IAtomReader R) =>
        R.GetColumns(DatasetId).Select(c => DataCell.ById.Ref(new CellId(Id, c.Id)));

    /// <summary>
    /// A row is inside its dataset.
    /// </summary>
    public override IEnumerable<AtomRef> Containers(IAtomReader R) => [Dataset.ById.Ref(DatasetId)];
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

    /// <summary>
    /// Whether a cell's row and column are both in this Dataset.
    /// </summary>
    public static bool InGrid(IAtomReader R, CellId id) =>
        R.Get(DataRow.ById, id.Row) is { Deleted: false }
        && R.Get(DataColumn.ById, id.Column) != null;

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

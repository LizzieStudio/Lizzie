using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

[JsonName("Dataset")]
public record Dataset : Atom
{
    public static readonly Identity<Dataset, Ref<Dataset>> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Ref{T}.Create"/>.
    /// </summary>
    public required Ref<Dataset> Id { get; init; }

    public static readonly All<Dataset> All = new();

    public string Name { get; init; }
}

/// <summary>
/// A dataset column.
/// </summary>
[JsonName("DataColumn")]
public record DataColumn : Atom, IRanked
{
    public static readonly Identity<DataColumn, Ref<DataColumn>> ById = new(c => c.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Ref{T}.Create"/>.
    /// </summary>
    public required Ref<DataColumn> Id { get; init; }

    /// <summary>
    /// The columns of each dataset, in order.
    /// </summary>
    public static readonly Lookup<DataColumn, Ref<Dataset>> ByDataset = new(
        c => c.DatasetRef,
        IRanked.ByRank
    );

    /// <summary>
    /// The dataset this column belongs to.
    /// </summary>
    public Ref<Dataset> DatasetRef { get; init; }

    /// <summary>
    /// The position of this column in its dataset.
    /// </summary>
    public SnowRank Rank { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// A cell for each of the dataset's rows.
    /// </summary>
    public override IEnumerable<IAtomRef> Contents(IAtomReader R) =>
        R.Get(DataRow.ByDataset, DatasetRef).Select(r => new CellId(r.Id, Id)).Cast<IAtomRef>();

    /// <summary>
    /// A column is inside its dataset.
    /// </summary>
    public override IEnumerable<IAtomRef> Containers(IAtomReader R) => [DatasetRef];
}

/// <summary>
/// A single dataset row.
/// </summary>
[JsonName("DataRow")]
public record DataRow : Atom, IRanked
{
    public static readonly Identity<DataRow, Ref<DataRow>> ById = new(r => r.Id);

    /// <summary>
    /// This atom's identity, created with <see cref="Ref{T}.Create"/>.
    /// </summary>
    public required Ref<DataRow> Id { get; init; }

    /// <summary>
    /// The rows of each dataset that aren't deleted, in order.
    /// </summary>
    public static readonly Lookup<DataRow, Ref<Dataset>?> ByDataset = new(
        r => r.Deleted ? null : r.DatasetRef,
        IRanked.ByRank
    );

    /// <summary>
    /// The dataset this row belongs to.
    /// </summary>
    public Ref<Dataset> DatasetRef { get; init; }

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
    public override IEnumerable<IAtomRef> Contents(IAtomReader R) =>
        R.Get(DataColumn.ByDataset, DatasetRef).Select(c => new CellId(Id, c.Id)).Cast<IAtomRef>();

    /// <summary>
    /// A row is inside its dataset.
    /// </summary>
    public override IEnumerable<IAtomRef> Containers(IAtomReader R) => [DatasetRef];
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
        R.Get(DataRow.ById, id.RowRef) is { Deleted: false }
        && R.Get(DataColumn.ById, id.ColumnRef) != null;

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
    public override IEnumerable<IAtomRef> Containers(IAtomReader R) => [Id.RowRef, Id.ColumnRef];
}

/// <summary>
/// The Id of a <see cref="DataCell"/>.
/// </summary>
public readonly record struct CellId(Ref<DataRow> RowRef, Ref<DataColumn> ColumnRef)
    : IAtomRef<DataCell>,
        IComparable<CellId>
{
    public int CompareTo(CellId other) =>
        RowRef.CompareTo(other.RowRef) is var c and not 0
            ? c
            : ColumnRef.CompareTo(other.ColumnRef);
}

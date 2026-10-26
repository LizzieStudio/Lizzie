using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Commands for a dataset's rows, columns, and cells.
/// </summary>
public static class DatasetCommands
{
    /// <summary>
    /// Adds a row after the last. The dataset editor's add buttons and Enter on its last row run it.
    /// </summary>
    [Command("dataset.add_row")]
    public static readonly Command AddRow = new AtomCommand<Dataset>
    {
        Icon = CommandIcons.Add,
        Caption = "Add Row",
        MaxTargets = 1,
        ActsOn = Context.Subject,
        Effects = (R, sets) => NewRow(R, sets[0].Id, R.GetRows(sets[0].Id).Count),
    };

    /// <summary>
    /// Adds a column after the last. The dataset editor's add buttons run it.
    /// </summary>
    [Command("dataset.add_column")]
    public static readonly Command AddColumn = new AtomCommand<Dataset>
    {
        Icon = CommandIcons.Add,
        Caption = "Add Column",
        MaxTargets = 1,
        ActsOn = Context.Subject,
        Effects = (R, sets) => NewColumn(R, sets[0].Id, R.GetColumns(sets[0].Id).Count),
    };

    [Command("dataset.update_decks")]
    public static readonly Command UpdateDecks = new AtomCommand<Dataset>
    {
        Icon = CommandIcons.UpdateDecks,
        Caption = "Update Decks",
        Tooltip = """
            Updates decks that show this Dataset:
            * New cards are added to the bottom of decks.
            * Updated cards are left in place.
            * Retired cards are removed.
            """,
        MaxTargets = 1,
        ActsOn = Context.Subject,
        Requires =
        [
            (
                "A prototype that shows this dataset now has different numbers of items on its front and back.",
                (R, set) => Showing(R, set.Id).All(p => p.Parameters.SidesFit(R))
            ),
        ],
        Effects = (R, sets) =>
            Showing(R, sets[0].Id)
                .SelectMany(p => DeckReconciliation.Changes(R, p.Id, p.Parameters))
                .ToList(),
    };

    /// <summary>
    /// The printed prototypes with a side that shows <paramref name="dataset"/>.
    /// </summary>
    private static IEnumerable<(Ref<Prototype> Id, PrintedParameters Parameters)> Showing(
        IAtomReader R,
        Ref<Dataset> dataset
    ) =>
        R.Get(Prototype.Live)
            .Where(p =>
                p.Parameters is PrintedParameters printed
                && (
                    printed.Front is TemplateSide front && front.DatasetRef == dataset
                    || printed.Back is TemplateSide back && back.DatasetRef == dataset
                )
            )
            .Select(p => (p.Id, (PrintedParameters)p.Parameters));

    [Command("dataset.edit_row")]
    public static readonly Command EditRow = new AtomCommand<DataRow>
    {
        Icon = CommandIcons.Edit,
        Caption = "Edit Data Row",
        MaxTargets = 1,
        ActsOn = Context.Referenced,
        AppliesTo = (_, row) => !row.Deleted,
        Effects = (_, rows) =>
        {
            EventBus.Instance.Publish(
                new ShowDatasetEditor
                {
                    DatasetRef = rows[0].DatasetRef,
                    SelectedRowRef = rows[0].Id,
                }
            );
            return null;
        },
    };

    [Command("dataset.delete_row")]
    public static readonly Command DeleteRow = new AtomCommand<DataRow>
    {
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Noun = ("Row", "Rows"),
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Containers,
        AppliesTo = (_, row) => !row.Deleted,
        Effects = (R, rows) => rows.Select(row => row with { Deleted = true }),
    };

    [Command("dataset.delete_column")]
    public static readonly Command DeleteColumn = new AtomCommand<DataColumn>
    {
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Noun = ("Column", "Columns"),
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Containers,
        Effects = (R, columns) => columns.Select(R.Unset),
    };

    [Command("dataset.clear_cells")]
    public static readonly Command ClearCells = new AtomCommand<DataCell>
    {
        Icon = CommandIcons.Clear,
        Caption = "Clear {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Key(Key.Delete)],
        Effects = Cleared,
    };

    [Command("dataset.cut_cells")]
    public static readonly Command CutCells = new AtomCommand<DataCell>
    {
        Icon = CommandIcons.Cut,
        Caption = "Cut {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Ctrl(Key.X)],
        Effects = (R, cells) =>
        {
            DisplayServer.ClipboardSet(Copied(R, cells));
            return Cleared(R, cells);
        },
    };

    [Command("dataset.copy_cells")]
    public static readonly Command CopyCells = new AtomCommand<DataCell>
    {
        Icon = CommandIcons.Copy,
        Caption = "Copy {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Ctrl(Key.C)],
        Effects = (R, cells) =>
        {
            DisplayServer.ClipboardSet(Copied(R, cells));
            return null;
        },
    };

    /// <summary>
    /// Writes the clipboard's cells from the selection's top-left cell, rightward and downward.
    /// Values past the last column are dropped, and rows past the last row become new rows.
    /// A single value fills every selected cell instead.
    /// </summary>
    [Command("dataset.paste_cells")]
    public static readonly Command PasteCells = new AtomCommand<DataCell>
    {
        Icon = CommandIcons.Paste,
        Caption = "Paste",
        Keys = [Shortcuts.Ctrl(Key.V)],
        Effects = (R, cells) => Pasted(R, cells, Tsv.Parse(DisplayServer.ClipboardGet())),
    };

    private static (
        List<DataRow> Rows,
        List<Ref<DataColumn>> Columns,
        List<DataRow> AllRows,
        List<Ref<DataColumn>> AllColumns
    ) Block(IAtomReader R, IReadOnlyList<DataCell> cells)
    {
        var datasetId = R.Get(DataRow.ById, cells[0].Id.RowRef).DatasetRef;
        var allRows = R.GetRows(datasetId);
        var allColumns = R.GetColumns(datasetId).Select(c => c.Id).ToList();
        var rowIds = cells.Select(c => c.Id.RowRef).ToHashSet();
        var columnIds = cells.Select(c => c.Id.ColumnRef).ToHashSet();
        return (
            allRows.Where(r => rowIds.Contains(r.Id)).ToList(),
            allColumns.Where(columnIds.Contains).ToList(),
            allRows,
            allColumns
        );
    }

    private static string Copied(IAtomReader R, IReadOnlyList<DataCell> cells)
    {
        var (rows, columns, _, _) = Block(R, cells);
        var selected = cells.ToDictionary(c => c.Id, c => c.Value);
        return Tsv.Format(
            rows.Select(r =>
                columns.Select(c => selected.GetValueOrDefault(new CellId(r.Id, c), string.Empty))
            )
        );
    }

    private static IEnumerable<IChange> Pasted(
        IAtomReader R,
        IReadOnlyList<DataCell> cells,
        List<string[]> grid
    )
    {
        if (grid is [[var value]])
            return cells.SelectMany(c => DataCell.Set(R, c.Id, value));

        var (rows, columns, allRows, allColumns) = Block(R, cells);
        int top = allRows.IndexOf(rows[0]);
        int left = allColumns.IndexOf(columns[0]);
        var rank = allRows[^1].Rank;

        var changes = new List<IChange>();
        for (int r = 0; r < grid.Count; r++)
        {
            var row = allRows.ElementAtOrDefault(top + r);
            if (row == null)
            {
                row = new DataRow
                {
                    Id = Ref<DataRow>.Create(),
                    DatasetRef = rows[0].DatasetRef,
                    Rank = rank = rank.After(SnowportId.Create()),
                };
                changes.Add(row);
            }
            for (int c = 0; c < grid[r].Length && left + c < allColumns.Count; c++)
                changes.AddRange(
                    DataCell.Set(R, new CellId(row.Id, allColumns[left + c]), grid[r][c])
                );
        }
        return changes;
    }

    private static IEnumerable<IChange> Cleared(IAtomReader R, IReadOnlyList<DataCell> cells) =>
        cells.SelectMany(c => DataCell.Set(R, c.Id, string.Empty));

    public static IEnumerable<Atom> NewRow(IAtomReader R, Ref<Dataset> datasetId, int index) =>
        [
            new DataRow
            {
                Id = Ref<DataRow>.Create(),
                DatasetRef = datasetId,
                Rank = SnowRank.At(R.GetRows(datasetId), index, SnowportId.Create()),
            },
        ];

    public static IEnumerable<Atom> NewColumn(IAtomReader R, Ref<Dataset> datasetId, int index)
    {
        var columns = R.GetColumns(datasetId);
        return
        [
            new DataColumn
            {
                Id = Ref<DataColumn>.Create(),
                DatasetRef = datasetId,
                Rank = SnowRank.At(columns, index, SnowportId.Create()),
                Name = $"Column {columns.Count + 1}",
            },
        ];
    }
}

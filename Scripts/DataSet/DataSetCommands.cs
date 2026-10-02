using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Commands for a dataset's rows, columns, and cells.
/// </summary>
public static class DataSetCommands
{
    private const string DeleteIcon = "res://Textures/UI/delete.svg";

    /// <summary>Adds a row after the last. The dataset editor's add buttons and Enter on its last row run it.</summary>
    public static readonly Command AddRow = new RecordCommand<DataSet>
    {
        Name = new("dataset.add_row"),
        Caption = "Add Row",
        Count = TargetCount.One,
        ActsOn = Context.View,
        ShowInMenu = false,
        Effects = (R, sets, _) =>
            NewRow(sets[0].Id, RowRank.New(R.LastRank(sets[0].Id), null, Snowport.Clock.source)),
    };

    /// <summary>Adds a column after the last. The dataset editor's add buttons run it.</summary>
    public static readonly Command AddColumn = new RecordCommand<DataSet>
    {
        Name = new("dataset.add_column"),
        Caption = "Add Column",
        Count = TargetCount.One,
        ActsOn = Context.View,
        ShowInMenu = false,
        Effects = (R, sets, _) => NewColumn(sets[0], sets[0].Columns.Length),
    };

    public static readonly Command EditRow = new RecordCommand<DataRow>
    {
        Name = new("dataset.edit_row"),
        Icon = UI.TextureUI_Pencil,
        Caption = "Edit Data Row",
        Count = TargetCount.One,
        ActsOn = Context.Referenced,
        SideEffects = (rows, _) =>
            EventBus.Instance.Publish(
                new ShowDatasetEditor
                {
                    DatasetRef = rows[0].DataSetId,
                    SelectedRowRef = rows[0].Id,
                }
            ),
    };

    public static readonly Command DeleteRow = new RecordCommand<DataRow>
    {
        Name = new("dataset.delete_row"),
        Icon = DeleteIcon,
        Caption = "Delete {0}",
        Noun = ("Row", "Rows"),
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Containers,
        Effects = (R, rows, _) => Effect.UpsertAll(rows.Select(r => r with { Deleted = true })),
    };

    /// <summary>Removes the columns from their datasets. The rows keep their values, so undo brings them back.</summary>
    public static readonly Command DeleteColumn = new TargetCommand<ColumnTarget>
    {
        Name = new("dataset.delete_column"),
        Icon = DeleteIcon,
        Caption = "Delete {0}",
        Noun = ("Column", "Columns"),
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Containers,
        AppliesTo = (R, t) => HasColumn(R.Get<DataSet>(t.DataSetId), t.ColumnId),
        Effects = (R, columns, _) =>
            columns
                .GroupBy(t => t.DataSetId)
                .Select(g =>
                {
                    var ds = R.Get<DataSet>(g.Key);
                    var ids = g.Select(t => t.ColumnId).ToHashSet();
                    return Effect.Upsert(
                        ds with
                        {
                            Columns = ds.Columns.RemoveAll(c => ids.Contains(c.Id)),
                        }
                    );
                }),
    };

    public static readonly Command ClearCells = new TargetCommand<CellTarget>
    {
        Name = new("dataset.clear_cells"),
        Icon = DeleteIcon,
        Caption = "Clear {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Key(Key.Delete)],
        AppliesTo = CellExists,
        Effects = (R, cells, _) => Cleared(R, cells),
    };

    public static readonly Command CutCells = new TargetCommand<CellTarget>
    {
        Name = new("dataset.cut_cells"),
        Caption = "Cut {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Ctrl(Key.X)],
        AppliesTo = CellExists,
        // Copied first, since side effects run before the effects are made.
        SideEffects = (cells, _) => DisplayServer.ClipboardSet(Copied(Reader, cells)),
        Effects = (R, cells, _) => Cleared(R, cells),
    };

    public static readonly Command CopyCells = new TargetCommand<CellTarget>
    {
        Name = new("dataset.copy_cells"),
        Icon = UI.TextureUI_ContentCopy,
        Caption = "Copy {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Ctrl(Key.C)],
        AppliesTo = CellExists,
        SideEffects = (cells, _) => DisplayServer.ClipboardSet(Copied(Reader, cells)),
    };

    /// <summary>
    /// Writes the clipboard's cells from the selection's top-left cell, rightward and downward.
    /// Values past the last column are dropped, and rows past the last row become new rows.
    /// A single value fills every selected cell instead.
    /// </summary>
    public static readonly Command PasteCells = new TargetCommand<CellTarget>
    {
        Name = new("dataset.paste_cells"),
        Caption = "Paste",
        Keys = [Shortcuts.Ctrl(Key.V)],
        AppliesTo = CellExists,
        Effects = (R, cells, _) => Pasted(R, cells, Tsv.Parse(DisplayServer.ClipboardGet())),
    };

    private static IRecordReader Reader => ProjectService.Instance;

    private static bool HasColumn(DataSet ds, SnowTag columnId) =>
        ds?.Columns.Any(c => c.Id == columnId) == true;

    private static bool CellExists(IRecordReader R, CellTarget t) =>
        R.Get<DataRow>(t.RowId) is { } row && HasColumn(R.Get<DataSet>(row.DataSetId), t.ColumnId);

    /// <summary>The rows and columns the cells span, in the grid's order, and the dataset's rows and columns.</summary>
    private static (
        List<DataRow> Rows,
        List<SnowTag> Columns,
        List<DataRow> AllRows,
        List<SnowTag> AllColumns
    ) Block(IRecordReader R, IReadOnlyList<CellTarget> cells)
    {
        var dataSetId = R.Get<DataRow>(cells[0].RowId).DataSetId;
        var allRows = R.GetRows(dataSetId);
        var allColumns = R.Get<DataSet>(dataSetId).Columns.Select(c => c.Id).ToList();
        var rowIds = cells.Select(c => c.RowId).ToHashSet();
        var columnIds = cells.Select(c => c.ColumnId).ToHashSet();
        return (
            allRows.Where(r => rowIds.Contains(r.Id)).ToList(),
            allColumns.Where(columnIds.Contains).ToList(),
            allRows,
            allColumns
        );
    }

    /// <summary>The cells as clipboard text: the block they span, with the cells not selected left empty.</summary>
    private static string Copied(IRecordReader R, IReadOnlyList<CellTarget> cells)
    {
        var (rows, columns, _, _) = Block(R, cells);
        var selected = cells.ToHashSet();
        return Tsv.Format(
            rows.Select(r =>
                columns.Select(c =>
                    selected.Contains(new CellTarget(r.Id, c))
                        ? r.Data.GetValueOrDefault(c, string.Empty)
                        : string.Empty
                )
            )
        );
    }

    private static IEnumerable<Effect> Pasted(
        IRecordReader R,
        IReadOnlyList<CellTarget> cells,
        List<string[]> grid
    )
    {
        if (grid is [[var value]])
            return Written(R, cells.Select(c => (c, value)));

        var (rows, columns, allRows, allColumns) = Block(R, cells);
        int top = allRows.IndexOf(rows[0]);
        int left = allColumns.IndexOf(columns[0]);
        // New rows go after every rank used so far, even deleted rows', which undo could bring back.
        string rank = R.LastRank(rows[0].DataSetId);

        var written = new List<DataRow>();
        for (int r = 0; r < grid.Count; r++)
        {
            var row =
                top + r < allRows.Count
                    ? allRows[top + r]
                    : new DataRow
                    {
                        Id = Snowport.Clock.CreateTag(),
                        DataSetId = rows[0].DataSetId,
                        Rank = rank = RowRank.New(rank, null, Snowport.Clock.source),
                    };
            for (int c = 0; c < grid[r].Length && left + c < allColumns.Count; c++)
                row = row.WithCell(allColumns[left + c], grid[r][c]);
            written.Add(row);
        }
        return Effect.UpsertAll(written);
    }

    private static IEnumerable<Effect> Cleared(IRecordReader R, IReadOnlyList<CellTarget> cells) =>
        Written(R, cells.Select(c => (c, string.Empty)));

    /// <summary>The rows with each cell set to its value, one effect per row.</summary>
    private static IEnumerable<Effect> Written(
        IRecordReader R,
        IEnumerable<(CellTarget Cell, string Value)> values
    ) =>
        Effect.UpsertAll(
            values
                .GroupBy(v => v.Cell.RowId)
                .Select(g =>
                    g.Aggregate(
                        R.Get<DataRow>(g.Key),
                        (row, v) => row.WithCell(v.Cell.ColumnId, v.Value)
                    )
                )
        );

    /// <summary>Adds an empty row to the dataset at <paramref name="rank"/>.</summary>
    public static IEnumerable<Effect> NewRow(SnowTag dataSetId, string rank) =>
        [
            Effect.Upsert(
                new DataRow
                {
                    Id = Snowport.Clock.CreateTag(),
                    DataSetId = dataSetId,
                    Rank = rank,
                }
            ),
        ];

    /// <summary>Adds a column to the dataset at <paramref name="index"/>, named by how many there are.</summary>
    public static IEnumerable<Effect> NewColumn(DataSet ds, int index)
    {
        var column = new Column
        {
            Id = Snowport.Clock.CreateTag(),
            Name = $"Column {ds.Columns.Length + 1}",
        };
        return [Effect.Upsert(ds with { Columns = ds.Columns.Insert(index, column) })];
    }
}

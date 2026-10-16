using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Commands for a dataset's rows, columns, and cells.
/// </summary>
public static class DataSetCommands
{
    /// <summary>
    /// Adds a row after the last. The dataset editor's add buttons and Enter on its last row run it.
    /// </summary>
    public static readonly Command AddRow = new RecordCommand<DataSet>
    {
        Name = new("dataset.add_row"),
        Icon = CommandIcons.Add,
        Caption = "Add Row",
        Count = TargetCount.One,
        ActsOn = Context.View,
        ShowInMenu = false,
        Effects = (R, sets, _) => NewRow(R, sets[0].Id, R.GetRows(sets[0].Id).Count),
    };

    /// <summary>
    /// Adds a column after the last. The dataset editor's add buttons run it.
    /// </summary>
    public static readonly Command AddColumn = new RecordCommand<DataSet>
    {
        Name = new("dataset.add_column"),
        Icon = CommandIcons.Add,
        Caption = "Add Column",
        Count = TargetCount.One,
        ActsOn = Context.View,
        ShowInMenu = false,
        Effects = (R, sets, _) => NewColumn(R, sets[0].Id, R.GetColumns(sets[0].Id).Count),
    };

    public static readonly Command EditRow = new RecordCommand<DataRow>
    {
        Name = new("dataset.edit_row"),
        Icon = CommandIcons.Edit,
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
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Noun = ("Row", "Rows"),
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Containers,
        Effects = (R, rows, _) => rows.Select(R.Unset),
    };

    public static readonly Command DeleteColumn = new RecordCommand<DataColumn>
    {
        Name = new("dataset.delete_column"),
        Icon = CommandIcons.Delete,
        Caption = "Delete {0}",
        Noun = ("Column", "Columns"),
        Keys = [Shortcuts.Key(Key.Delete)],
        ActsOn = Context.Selected | Context.Containers,
        Effects = (R, columns, _) => columns.Select(R.Unset),
    };

    public static readonly Command ClearCells = new RecordCommand<DataCell>
    {
        Name = new("dataset.clear_cells"),
        Icon = CommandIcons.Clear,
        Caption = "Clear {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Key(Key.Delete)],
        AppliesTo = CellExists,
        Effects = (R, cells, _) => Cleared(R, cells),
    };

    public static readonly Command CutCells = new RecordCommand<DataCell>
    {
        Name = new("dataset.cut_cells"),
        Icon = CommandIcons.Cut,
        Caption = "Cut {0}",
        Noun = ("Cell", "Cells"),
        Keys = [Shortcuts.Ctrl(Key.X)],
        AppliesTo = CellExists,
        // Copied first, since side effects run before the effects are made.
        SideEffects = (cells, _) => DisplayServer.ClipboardSet(Copied(Reader, cells)),
        Effects = (R, cells, _) => Cleared(R, cells),
    };

    public static readonly Command CopyCells = new RecordCommand<DataCell>
    {
        Name = new("dataset.copy_cells"),
        Icon = CommandIcons.Copy,
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
    public static readonly Command PasteCells = new RecordCommand<DataCell>
    {
        Name = new("dataset.paste_cells"),
        Icon = CommandIcons.Paste,
        Caption = "Paste",
        Keys = [Shortcuts.Ctrl(Key.V)],
        AppliesTo = CellExists,
        Effects = (R, cells, _) => Pasted(R, cells, Tsv.Parse(DisplayServer.ClipboardGet())),
    };

    private static IRecordReader Reader => RecordService.Instance;

    // A cell can be empty, but its row and column must still be there.
    private static bool CellExists(IRecordReader R, DataCell cell) =>
        R.Get(DataRow.ById, cell.Id.Row) != null && R.Get(DataColumn.ById, cell.Id.Column) != null;

    private static (
        List<DataRow> Rows,
        List<SnowTag> Columns,
        List<DataRow> AllRows,
        List<SnowTag> AllColumns
    ) Block(IRecordReader R, IReadOnlyList<DataCell> cells)
    {
        var dataSetId = R.Get(DataRow.ById, cells[0].Id.Row).DataSetId;
        var allRows = R.GetRows(dataSetId);
        var allColumns = R.GetColumns(dataSetId).Select(c => c.Id).ToList();
        var rowIds = cells.Select(c => c.Id.Row).ToHashSet();
        var columnIds = cells.Select(c => c.Id.Column).ToHashSet();
        return (
            allRows.Where(r => rowIds.Contains(r.Id)).ToList(),
            allColumns.Where(columnIds.Contains).ToList(),
            allRows,
            allColumns
        );
    }

    private static string Copied(IRecordReader R, IReadOnlyList<DataCell> cells)
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
        IRecordReader R,
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
                    Id = Snowport.Clock.CreateTag(),
                    DataSetId = rows[0].DataSetId,
                    Rank = rank = rank.After(Snowport.Clock.Create()),
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

    private static IEnumerable<IChange> Cleared(IRecordReader R, IReadOnlyList<DataCell> cells) =>
        cells.SelectMany(c => DataCell.Set(R, c.Id, string.Empty));

    public static IEnumerable<Replicated> NewRow(IRecordReader R, SnowTag dataSetId, int index) =>
        [
            new DataRow
            {
                Id = Snowport.Clock.CreateTag(),
                DataSetId = dataSetId,
                Rank = SnowRank.At(R.GetRows(dataSetId), index, Snowport.Clock.Create()),
            },
        ];

    public static IEnumerable<Replicated> NewColumn(IRecordReader R, SnowTag dataSetId, int index)
    {
        var columns = R.GetColumns(dataSetId);
        return
        [
            new DataColumn
            {
                Id = Snowport.Clock.CreateTag(),
                DataSetId = dataSetId,
                Rank = SnowRank.At(columns, index, Snowport.Clock.Create()),
                Name = $"Column {columns.Count + 1}",
            },
        ];
    }
}

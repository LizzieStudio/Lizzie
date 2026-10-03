using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

public partial class DatasetEditor : Window, ICommandView
{
    /// <summary>One displayed row.</summary>
    private sealed class RowView
    {
        public HBoxContainer Box;
        public PanelContainer Header;
        public Label Number;
        public readonly List<LineEdit> Cells = new();
        public DataRow Row;

        /// <summary>What clicking its header selects.</summary>
        public RecordTarget Target => new(Row.Id);
    }

    private DataSet _currentDataSet;

    private VBoxContainer _mainContainer;
    private Button _newButton;
    private DataSetSelector _datasetList;
    private Button _linkButton;

    private ScrollContainer _headerScroll;
    private HBoxContainer _headerContainer;
    private ScrollContainer _dataScrollContainer;
    private VBoxContainer _dataContainer;
    private ColorRect _dropIndicator;

    private ConfirmationDialog _newDatasetDialog;
    private LineEdit _newDatasetNameInput;
    private Label _newDatasetErrorLabel;

    // The grid is built for one dataset's column layout and rebuilt when either changes.
    private SnowTag _shownDataSetId = SnowTag.Empty;
    private List<SnowTag> _columnIds = new();
    private readonly List<HeaderCell> _headerCells = new();
    private readonly Dictionary<SnowTag, float> _columnWidths = new();

    // Rows are reconciled by id so edits don't lose focus.
    private List<RowView> _views = new();

    // The line under the last row, holding only a button that adds a row.
    private HBoxContainer _addRowLine;

    private SnowTag _revealRowOnSync = SnowTag.Empty;

    // The columns the last grid rebuild added to the shown dataset, for an insert's listener to rename.
    private List<SnowTag> _addedColumns = [];

    // Whether a cell is being edited. Updated once focus settles: ShortcutRelay drops focus on a click
    // outside the focused cell before this window sees it, and the grid should still be editing,
    // so the click edits the cell it lands on.
    private bool _editing;

    // Where Shift+click selects from: the last row, column or cell selected without Shift.
    private Target _anchor;

    // The theme's styles tinted for each highlight color, shared by every cell or header.
    private readonly Dictionary<(string Style, Color Color), StyleBox> _highlightStyles = new();

    // The pointer during a row or column drag, for auto-scrolling at the edges, and the axis it drags along.
    private Vector2? _dragAt;
    private Vector2.Axis _dragAxis;

    private IReadOnlyList<Command> _commands;

    // The drop indicator's color.
    private static readonly Color Accent = Color.FromHtml("8cb1ff");

    // The local player's own selection shows white, as on the table, so it's never mistaken for a player's color.

    private const float RowHeaderWidth = 48f;
    private const float DefaultColumnWidth = 120f;
    private const float MinColumnWidth = 50f;
    private const float HeaderHeight = 30f;
    private const float RowHeight = 30f;
    private const float ScrollbarAllowance = 24f;
    private const float IndicatorThickness = 3f;
    private const float AutoScrollMargin = 24f;
    private const float AutoScrollSpeed = 600f;

    public override void _Ready()
    {
        InitializeSpreadsheet();
        _commands = EditorCommands();

        CloseRequested += CloseDialog;
        MoveToCenter();
    }

    public override void _EnterTree()
    {
        var ps = RecordService.Instance;
        ps.Watch(this, Sync);

        // The local player's inserts focus the row they add, or start renaming the column,
        // once the grid shows it. A row insert writes only the new row.
        foreach (
            var insert in new Command[] { InsertRowAbove, InsertRowBelow, DataSetCommands.AddRow }
        )
            ps.Listen(
                this,
                insert,
                e =>
                    FocusCell(
                        ViewOf(e.Effects.OfType<UpdateReplicatedEffect<DataRow>>().Single().Id),
                        0
                    )
            );

        foreach (
            var insert in new Command[]
            {
                InsertColumnLeft,
                InsertColumnRight,
                DataSetCommands.AddColumn,
            }
        )
            ps.Listen(
                this,
                insert,
                _ =>
                    _headerCells
                        .FirstOrDefault(h => _addedColumns.Contains(h.ColumnId))
                        ?.BeginRename()
            );
    }

    #region ICommandView

    /// <summary>The shown dataset.</summary>
    public WatchableValue<SnowTag> SelectionScope { get; } = new();

    public IReadOnlyList<Command> Commands => _commands;

    /// <summary>Undo walks the shown dataset's edits, its rows', and what's selected in it.</summary>
    public bool UndoScope(Effect fx) =>
        DatasetRef != SnowTag.Empty
        && (
            fx is UpdateReplicatedEffect<DataSet> ds && ds.Id == DatasetRef
            || fx is UpdateReplicatedEffect<DataRow> row && row.Payload?.DataSetId == DatasetRef
            || fx is UpdateReplicatedEffect<Selection> s && s.Payload?.Within == DatasetRef
        );

    #endregion

    public override void _Process(double delta)
    {
        if (_dragAt is not Vector2 at)
            return;

        // Holding a drag near an edge scrolls toward it.
        int i = (int)_dragAxis;
        var rect = _dataScrollContainer.GetGlobalRect();
        int step = (int)(AutoScrollSpeed * delta);
        int direction =
            at[i] < rect.Position[i] + AutoScrollMargin ? -1
            : at[i] > rect.End[i] - AutoScrollMargin ? 1
            : 0;
        if (_dragAxis == Vector2.Axis.Y)
            _dataScrollContainer.ScrollVertical += direction * step;
        else
            _dataScrollContainer.ScrollHorizontal += direction * step;
        ShowDrop(_dragAxis, at);
    }

    // Cells only show their values until one is being edited, so clicks on them select instead,
    // a right press included, for the command menu. Double-clicking one edits it.
    // While editing, every cell is a text box, but Shift or Ctrl+click still selects without editing.
    // Handled before the GUI, since a click would focus the cell, which edits it and selects it alone.
    public override void _Input(InputEvent e)
    {
        if (
            e
                is not InputEventMouseButton
                {
                    ButtonIndex: MouseButton.Left or MouseButton.Right
                } click
            || CellOf(GuiGetHoveredControl()) is not { } cell
        )
            return;

        bool modified = click.ShiftPressed || click.IsCommandOrControlPressed();
        if (_editing && !(modified && click.ButtonIndex == MouseButton.Left))
            return;

        if (click is { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            SelectTarget(cell, click);
            if (click.DoubleClick && !modified)
                BeginEdit(cell);
        }
        else if (click is { ButtonIndex: MouseButton.Right, Pressed: true })
            SelectForMenu(cell);
        SetInputAsHandled();
    }

    // Cells consume these keys while focused, so this only sees them when nothing is being edited.
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key || key.GetModifiersMask() != 0)
            return;

        switch (key.Keycode)
        {
            case Key.Escape when !MySelection().IsEmpty:
                Select([]);
                break;
            // Like a spreadsheet, Enter edits the selection's top-left cell.
            case Key.Enter or Key.KpEnter when TopLeftCell() is { } cell:
                BeginEdit(cell);
                break;
            default:
                return;
        }
        SetInputAsHandled();
    }

    private void Sync(IRecordReader R)
    {
        // It opens on the first dataset when none is chosen. A chosen one that's gone was undone,
        // so the editor stays on it, where redo brings it back.
        var ds = SelectionScope.TryGet(R, out var id)
            ? R.Get<DataSet>(id)
            : R.Get<DataSet>().FirstOrDefault();
        if (ds != null)
            SelectionScope.Set(ds.Id);

        _datasetList.SelectedDataSet = DatasetRef;
        _currentDataSet = ds;

        if (ds == null)
            ClearGrid();
        else
            ShowDataSet(R, ds);
    }

    // The shown dataset's id, or empty.
    private SnowTag DatasetRef => SelectionScope.TryGet(out var id) ? id : SnowTag.Empty;

    private void ShowDataSet(IRecordReader R, DataSet ds)
    {
        var columnIds = ds.Columns.Select(c => c.Id).ToList();
        if (ds.Id != _shownDataSetId || !columnIds.SequenceEqual(_columnIds))
        {
            _addedColumns = ds.Id == _shownDataSetId ? columnIds.Except(_columnIds).ToList() : [];
            ClearGrid();
            _shownDataSetId = ds.Id;
            _columnIds = columnIds;
            BuildGrid();
        }

        for (int i = 0; i < _headerCells.Count; i++)
            _headerCells[i].SetHeaderText(ds.Columns[i].Name);

        ReconcileRows(R.GetRows(ds.Id));
        SyncSelection(R, ds.Id);

        if (_revealRowOnSync != SnowTag.Empty)
        {
            if (_views.FirstOrDefault(v => v.Row.Id == _revealRowOnSync) is { } view)
            {
                // defer so the grid has time to complete its layout
                Callable
                    .From(() => _dataScrollContainer.EnsureControlVisible(view.Box))
                    .CallDeferred();
            }
            _revealRowOnSync = SnowTag.Empty;
        }
    }

    private void InitializeSpreadsheet()
    {
        _mainContainer = GetNode<VBoxContainer>("%MainContainer");
        _mainContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // The header scrolls horizontally with the data but stays put vertically.
        _headerScroll = new ScrollContainer();
        _mainContainer.AddChild(_headerScroll);
        _headerScroll.CustomMinimumSize = new Vector2(0, HeaderHeight);
        _headerScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        _headerScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;

        _headerContainer = new HBoxContainer();
        _headerScroll.AddChild(_headerContainer);

        _dataScrollContainer = new ScrollContainer();
        _mainContainer.AddChild(_dataScrollContainer);
        _dataScrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _dataScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
        _dataScrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _dataScrollContainer.FollowFocus = true;
        _dataScrollContainer.GetHScrollBar().ValueChanged += v =>
            _headerScroll.ScrollHorizontal = (int)v;

        _dataContainer = new VBoxContainer();
        _dataScrollContainer.AddChild(_dataContainer);

        _dropIndicator = new ColorRect();
        AddChild(_dropIndicator);
        _dropIndicator.TopLevel = true;
        _dropIndicator.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dropIndicator.Color = Accent;
        _dropIndicator.Visible = false;

        _linkButton = GetNode<Button>("%Link");
        _linkButton.Pressed += OnImportPressed;

        _newButton = GetNode<Button>("%New");
        _newButton.Pressed += OnNewDatasetPressed;

        _datasetList = GetNode<DataSetSelector>("%DatasetList");
        _datasetList.DataSetSelected += SetDatasetById;

        InitializeNewDatasetDialog();
    }

    /// <summary>Opens the editor on a specific dataset. Empty opens the first one.</summary>
    public void SetDatasetById(SnowTag id)
    {
        if (id == SnowTag.Empty)
            SelectionScope.Unset();
        else
            SelectionScope.Set(id);
    }

    /// <summary>
    /// Selects a row of the shown dataset and scrolls it into view.
    /// </summary>
    public void RevealRow(SnowTag rowId)
    {
        Select([new RecordTarget(rowId)]);
        _revealRowOnSync = rowId;
        RecordService.Instance.QueueSync(this);
    }

    private void OnNewDatasetPressed()
    {
        _newDatasetNameInput.Clear();
        _newDatasetErrorLabel.Text = string.Empty;
        _newDatasetDialog.GetOkButton().Disabled = true;
        _newDatasetDialog.PopupCentered();
    }

    private void InitializeNewDatasetDialog()
    {
        _newDatasetDialog = new ConfirmationDialog();
        _newDatasetDialog.Title = "New Dataset";
        _newDatasetDialog.OkButtonText = "Create";

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(300, 0);

        var label = new Label();
        label.Text = "Dataset name:";
        vbox.AddChild(label);

        _newDatasetNameInput = new LineEdit();
        _newDatasetNameInput.PlaceholderText = "Enter unique name...";
        _newDatasetNameInput.TextChanged += OnNewDatasetNameChanged;
        vbox.AddChild(_newDatasetNameInput);

        _newDatasetErrorLabel = new Label();
        _newDatasetErrorLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
        vbox.AddChild(_newDatasetErrorLabel);

        _newDatasetDialog.AddChild(vbox);
        _newDatasetDialog.Confirmed += OnNewDatasetConfirmed;
        AddChild(_newDatasetDialog);
    }

    private void OnNewDatasetNameChanged(string text)
    {
        var isEmpty = string.IsNullOrWhiteSpace(text);
        _newDatasetErrorLabel.Text = string.Empty;
        _newDatasetDialog.GetOkButton().Disabled = isEmpty;
    }

    private void OnNewDatasetConfirmed()
    {
        var name = _newDatasetNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var ds = new DataSet { Id = Snowport.Clock.CreateTag(), Name = name };
        ProjectService.Instance.Upsert(ds);

        SetDatasetById(ds.Id);
    }

    public event EventHandler Closed;

    private void CloseDialog()
    {
        // Writes the cell being edited.
        GuiReleaseFocus();
        Closed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private float ColumnWidth(SnowTag id) =>
        _columnWidths.GetValueOrDefault(id, DefaultColumnWidth);

    private ColumnTarget Column(SnowTag id) => new(DatasetRef, id);

    private void BuildGrid()
    {
        var corner = new Control();
        _headerContainer.AddChild(corner);
        corner.CustomMinimumSize = new Vector2(RowHeaderWidth, HeaderHeight);

        foreach (var id in _columnIds)
        {
            var header = new HeaderCell();
            _headerContainer.AddChild(header);
            header.ColumnId = id;
            header.CustomMinimumSize = new Vector2(ColumnWidth(id), HeaderHeight);
            header.WidthDragged += OnColumnWidthDragged;
            header.NameCommitted += OnColumnRenamed;
            header.Clicked += (columnId, click) => SelectTarget(Column(columnId), click);
            header.RightPressed += columnId => SelectForMenu(Column(columnId));
            header.ColumnDragMoved += (_, at) => OnDragMoved(Vector2.Axis.X, at);
            header.ColumnDropped += DropColumn;
            _headerCells.Add(header);
        }

        _headerContainer.AddChild(AddButton(DataSetCommands.AddColumn, HeaderHeight));

        // Lets the header scroll as far as the data, whose view is narrowed by its vertical scrollbar.
        var spacer = new Control();
        _headerContainer.AddChild(spacer);
        spacer.CustomMinimumSize = new Vector2(ScrollbarAllowance, 0);

        _addRowLine = new HBoxContainer();
        _dataContainer.AddChild(_addRowLine);
        _addRowLine.AddChild(AddButton(DataSetCommands.AddRow, RowHeaderWidth));
    }

    // A "+" after the last row or column, doing what the toolbar's button does.
    private CommandButton AddButton(Command command, float width) =>
        new()
        {
            Text = "+",
            ShowCaption = false,
            Command = command,
            CustomMinimumSize = new Vector2(width, RowHeight),
            FocusMode = Control.FocusModeEnum.None,
        };

    private void ClearGrid()
    {
        foreach (var view in _views)
            FreeRowView(view);
        _views = new List<RowView>();

        _addRowLine?.GetParent().RemoveChild(_addRowLine);
        _addRowLine?.QueueFree();
        _addRowLine = null;

        foreach (var c in _headerContainer.GetChildren())
        {
            _headerContainer.RemoveChild(c);
            c.QueueFree();
        }
        _headerCells.Clear();

        _shownDataSetId = SnowTag.Empty;
        _columnIds = new List<SnowTag>();
    }

    private RowView CreateRowView()
    {
        var view = new RowView { Box = new HBoxContainer() };
        _dataContainer.AddChild(view.Box);
        view.Box.CustomMinimumSize = new Vector2(0, RowHeight);

        // Like a column's header: click to select, drag to reorder.
        view.Header = new PanelContainer();
        view.Box.AddChild(view.Header);
        view.Header.CustomMinimumSize = new Vector2(RowHeaderWidth, RowHeight);
        view.Header.MouseDefaultCursorShape = Control.CursorShape.Move;

        var drag = new DragGesture(view.Header);
        drag.Clicked += click => SelectTarget(view.Target, click);
        drag.Moved += at => OnDragMoved(Vector2.Axis.Y, at);
        drag.Dropped += at => DropRow(view, at);
        drag.RightPressed += () => SelectForMenu(view.Target);

        view.Number = new Label();
        view.Header.AddChild(view.Number);
        view.Number.HorizontalAlignment = HorizontalAlignment.Center;
        view.Number.VerticalAlignment = VerticalAlignment.Center;
        view.Number.Modulate = new Color(1, 1, 1, 0.5f);

        for (int i = 0; i < _columnIds.Count; i++)
        {
            var column = i;
            var cell = new LineEdit();
            view.Box.AddChild(cell);
            cell.CustomMinimumSize = new Vector2(ColumnWidth(_columnIds[i]), RowHeight);
            cell.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            cell.SizeFlagsVertical = Control.SizeFlags.Fill;

            cell.FocusEntered += () => OnCellFocused(view, column);
            cell.FocusExited += () =>
            {
                CommitRow(view);
                // Once focus has settled, which may be on another cell.
                Callable.From(SyncEditing).CallDeferred();
            };
            cell.GuiInput += e => OnCellInput(view, column, e);

            view.Cells.Add(cell);
        }

        return view;
    }

    private static void FreeRowView(RowView view)
    {
        // Detach first: removing a focused cell fires FocusExited, which must not write the row.
        view.Row = null;
        view.Box.GetParent()?.RemoveChild(view.Box);
        view.Box.QueueFree();
    }

    private void BindRowView(RowView view, DataRow row, int index)
    {
        view.Row = row;
        view.Number.Text = (index + 1).ToString();

        for (int i = 0; i < view.Cells.Count; i++)
        {
            var cell = view.Cells[i];
            var value = row.Data.GetValueOrDefault(_columnIds[i], string.Empty);
            // Leave the cell being typed in alone; it commits when focus leaves.
            if (!cell.HasFocus() && cell.Text != value)
                cell.Text = value;
        }
    }

    private void ReconcileRows(IReadOnlyList<DataRow> rows)
    {
        var existing = _views.ToDictionary(v => v.Row.Id);
        var next = new List<RowView>(rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            if (!existing.Remove(rows[i].Id, out var view))
                view = CreateRowView();
            BindRowView(view, rows[i], i);
            _dataContainer.MoveChild(view.Box, i);
            next.Add(view);
        }

        foreach (var gone in existing.Values)
            FreeRowView(gone);

        _views = next;
        _dataContainer.MoveChild(_addRowLine, _dataContainer.GetChildCount() - 1);
        ShowEditing();
    }

    /// <summary>The cell <paramref name="control"/> shows, or null if it isn't one.</summary>
    private CellTarget CellOf(Control control)
    {
        foreach (var view in _views)
        {
            int column = view.Cells.IndexOf(control as LineEdit);
            if (column >= 0)
                return new CellTarget(view.Row.Id, _columnIds[column]);
        }
        return null;
    }

    /// <summary>The text box showing <paramref name="cell"/>, or null if it isn't shown.</summary>
    private LineEdit CellControl(CellTarget cell)
    {
        int column = _columnIds.IndexOf(cell.ColumnId);
        return column >= 0 ? ViewOf(cell.RowId)?.Cells[column] : null;
    }

    // Updates whether a cell is being edited, which makes every cell a text box.
    private void SyncEditing()
    {
        _editing = CellOf(GuiGetFocusOwner()) != null;
        ShowEditing();
    }

    // Cells show the text cursor only while editing, when a click places the caret.
    private void ShowEditing()
    {
        var shape = _editing ? Control.CursorShape.Ibeam : Control.CursorShape.Arrow;
        foreach (var view in _views)
        foreach (var cell in view.Cells)
            cell.MouseDefaultCursorShape = shape;
    }

    /// <summary>Starts editing <paramref name="cell"/>, with the caret after its text.</summary>
    private void BeginEdit(CellTarget cell)
    {
        if (CellControl(cell) is not { } edit)
            return;
        edit.GrabFocus();
        // After the select-all that focusing from the keyboard queues.
        Callable
            .From(() =>
            {
                edit.Deselect();
                edit.CaretColumn = edit.Text.Length;
            })
            .CallDeferred();
    }

    /// <summary>The top-left of the block of selected cells, or null without any.</summary>
    private CellTarget TopLeftCell()
    {
        var cells = MySelection().OfType<CellTarget>().ToList();
        var row = _views.FirstOrDefault(v => cells.Any(c => c.RowId == v.Row.Id));
        var column = _columnIds.FirstOrDefault(
            id => cells.Any(c => c.ColumnId == id),
            SnowTag.Empty
        );
        return row != null && column != SnowTag.Empty ? new CellTarget(row.Row.Id, column) : null;
    }

    private RowView Neighbor(RowView view, int step)
    {
        int i = _views.IndexOf(view) + step;
        return i >= 0 && i < _views.Count ? _views[i] : null;
    }

    private static void FocusCell(RowView view, int column)
    {
        if (view != null && column >= 0 && column < view.Cells.Count)
            view.Cells[column].GrabFocus();
    }

    private void OnCellFocused(RowView view, int column)
    {
        SyncEditing();

        // The cell being edited is selected, so it shows the highlight too.
        SelectTarget(new CellTarget(view.Row.Id, _columnIds[column]));

        // Keyboard navigation selects the whole cell, like a spreadsheet; a click places the caret.
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
            view.Cells[column].CallDeferred(LineEdit.MethodName.SelectAll);
    }

    private void OnCellInput(RowView view, int column, InputEvent e)
    {
        var cell = view.Cells[column];

        if (e is not InputEventKey { Pressed: true } key)
            return;

        // Cells copied from a spreadsheet spread across the grid; anything else pastes as usual.
        if (e.IsActionPressed("ui_paste"))
        {
            var text = DisplayServer.ClipboardGet().TrimEnd('\r', '\n');
            if (text.Contains('\t') || text.Contains('\n'))
            {
                // Writes the cell first, so the paste starts from what it shows.
                GuiReleaseFocus();
                RunOnSelection(DataSetCommands.PasteCells);
                cell.AcceptEvent();
            }
            return;
        }

        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                CommitRow(view);
                // Enter on the last row adds a row below it.
                if (!key.ShiftPressed && view == _views[^1])
                    AddRow();
                else
                    FocusCell(Neighbor(view, key.ShiftPressed ? -1 : 1), 0);
                break;
            case Key.Up:
                FocusCell(Neighbor(view, -1), column);
                break;
            case Key.Down:
                FocusCell(Neighbor(view, 1), column);
                break;
            // Like a spreadsheet, Escape drops the cell's change and stops editing, leaving it selected.
            case Key.Escape:
                cell.Text = view.Row.Data.GetValueOrDefault(_columnIds[column], string.Empty);
                GuiReleaseFocus();
                break;
            default:
                return;
        }
        cell.AcceptEvent();
    }

    private static bool DataEqual(
        IReadOnlyDictionary<SnowTag, string> a,
        IReadOnlyDictionary<SnowTag, string> b
    )
    {
        if (a.Count != b.Count)
            return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value)
                return false;
        return true;
    }

    /// <summary>Writes the row's cells, if they changed. Empty cells have no value.</summary>
    private void CommitRow(RowView view)
    {
        if (view.Row == null)
            return;

        var row = view.Row;
        for (int i = 0; i < view.Cells.Count; i++)
            row = row.WithCell(_columnIds[i], view.Cells[i].Text);
        if (DataEqual(row.Data, view.Row.Data))
            return;

        view.Row = row;
        ProjectService.Instance.Upsert(row);
    }

    /// <summary>Adds an empty row after the last, which its listener then focuses.</summary>
    private void AddRow() => DataSetCommands.AddRow.Run([new RecordTarget(DatasetRef)], 1, this);

    private RowView ViewOf(SnowTag rowId) => _views.FirstOrDefault(v => v.Row.Id == rowId);

    /// <summary>Moves <paramref name="row"/> so it sits at <paramref name="index"/> among the other rows.</summary>
    private void MoveRow(DataRow row, int index)
    {
        var others = _views.Select(v => v.Row).Where(r => r.Id != row.Id).ToList();
        if (others.Count == 0)
            return;

        var rank =
            index > 0
                ? RecordService.Instance.RankBeside(others[index - 1], after: true)
                : RecordService.Instance.RankBeside(others[0], after: false);
        ProjectService.Instance.Upsert(row with { Rank = rank });
    }

    // The rows' or the columns' controls, for a drag along that axis.
    private List<Control> DragItems(Vector2.Axis axis) =>
        axis == Vector2.Axis.Y
            ? _views.Select(v => (Control)v.Box).ToList()
            : _headerCells.Cast<Control>().ToList();

    // Where a dragged row or column would land: how many of the others are before the pointer.
    private int DropIndex(Vector2.Axis axis, Vector2 at) =>
        DragItems(axis).Count(c => c.GetGlobalRect().GetCenter()[(int)axis] < at[(int)axis]);

    private void OnDragMoved(Vector2.Axis axis, Vector2 at)
    {
        _dragAt = at;
        _dragAxis = axis;
        ShowDrop(axis, at);
    }

    /// <summary>
    /// Shows a line where a dragged row or column would land.
    /// A row's line runs across the data; a column's runs down the header and the data.
    /// </summary>
    private void ShowDrop(Vector2.Axis axis, Vector2 at)
    {
        var items = DragItems(axis);
        if (items.Count == 0)
            return;

        int i = (int)axis;
        int index = DropIndex(axis, at);
        float line =
            index < items.Count
                ? items[index].GetGlobalRect().Position[i]
                : items[^1].GetGlobalRect().End[i];

        var data = _dataScrollContainer.GetGlobalRect();
        var span = axis == Vector2.Axis.Y ? data : _headerScroll.GetGlobalRect().Merge(data);
        var position = span.Position;
        var size = span.Size;
        position[i] = Mathf.Clamp(line, span.Position[i], span.End[i]) - IndicatorThickness / 2;
        size[i] = IndicatorThickness;

        _dropIndicator.Position = position;
        _dropIndicator.Size = size;
        _dropIndicator.Visible = true;
    }

    private void EndDrag()
    {
        _dropIndicator.Visible = false;
        _dragAt = null;
    }

    private void DropRow(RowView view, Vector2 at)
    {
        EndDrag();

        int from = _views.IndexOf(view);
        if (from < 0)
            return;

        int to = DropIndex(Vector2.Axis.Y, at);
        if (to == from || to == from + 1)
            return;

        MoveRow(view.Row, to > from ? to - 1 : to);
    }

    private void DropColumn(SnowTag columnId, Vector2 at)
    {
        EndDrag();

        if (_currentDataSet == null)
            return;

        var columns = _currentDataSet.Columns;
        int from = columns.IndexOf(columns.FirstOrDefault(c => c.Id == columnId));
        if (from < 0)
            return;

        int to = DropIndex(Vector2.Axis.X, at);
        if (to > from)
            to--;
        if (to == from)
            return;

        _currentDataSet = _currentDataSet with
        {
            Columns = columns.RemoveAt(from).Insert(to, columns[from]),
        };
        ProjectService.Instance.Upsert(_currentDataSet);
    }

    private void OnColumnRenamed(SnowTag columnId, string name)
    {
        if (_currentDataSet == null)
            return;

        var columns = _currentDataSet.Columns;
        int index = columns.IndexOf(columns.FirstOrDefault(c => c.Id == columnId));
        if (index < 0)
            return;

        _currentDataSet = _currentDataSet with
        {
            Columns = columns.SetItem(index, columns[index] with { Name = name }),
        };
        ProjectService.Instance.Upsert(_currentDataSet);
    }

    /// <summary>What the local player has selected in the shown dataset.</summary>
    private ImmutableHashSet<Target> MySelection() =>
        DatasetRef == SnowTag.Empty
            ? ImmutableHashSet<Target>.Empty
            : RecordService.Instance.GetSelection(DatasetRef)?.Targets
                ?? ImmutableHashSet<Target>.Empty;

    /// <summary>Replaces what the local player has selected in the shown dataset.</summary>
    private void Select(IEnumerable<Target> targets)
    {
        if (DatasetRef != SnowTag.Empty)
            RecordService.Instance.SetSelection(targets, DatasetRef);
    }

    /// <summary>
    /// Selects a row, column or cell, as a click on it does. With Shift, it selects everything from the
    /// last one selected without Shift; with Ctrl (or Cmd), it's added or removed; otherwise it's selected alone.
    /// Only one kind is selected at a time, so Delete acts on rows or columns, never both.
    /// A click takes focus from the cell being edited, so keys like Delete go to the selection.
    /// Focusing a cell selects it without a click.
    /// </summary>
    private void SelectTarget(Target target, InputEventWithModifiers click = null)
    {
        var mine = MySelection();
        var kind = mine.Where(t => t.GetType() == target.GetType());
        if (click?.ShiftPressed == true && _anchor?.GetType() == target.GetType())
            Select(Range(_anchor, target));
        else
        {
            _anchor = target;
            Select(
                click?.IsCommandOrControlPressed() != true ? [target]
                : mine.Contains(target) ? kind.Where(t => t != target)
                : kind.Append(target)
            );
        }

        if (click != null)
            GuiReleaseFocus();
    }

    /// <summary>Everything from one row, column or cell to another, in the grid's order. Cells make a rectangle.</summary>
    private IEnumerable<Target> Range(Target from, Target to)
    {
        var rows = _views.Select(v => v.Row.Id).ToList();
        return (from, to) switch
        {
            (RecordTarget a, RecordTarget b) => Span(rows, a.Id, b.Id)
                .Select(id => (Target)new RecordTarget(id)),
            (ColumnTarget a, ColumnTarget b) => Span(_columnIds, a.ColumnId, b.ColumnId)
                .Select(id => (Target)Column(id)),
            (CellTarget a, CellTarget b) => Span(rows, a.RowId, b.RowId)
                .SelectMany(r =>
                    Span(_columnIds, a.ColumnId, b.ColumnId)
                        .Select(c => (Target)new CellTarget(r, c))
                ),
            _ => [to],
        };
    }

    // The ids from one to the other, both included, in order. Just the last if either is gone.
    private static IEnumerable<SnowTag> Span(List<SnowTag> order, SnowTag from, SnowTag to)
    {
        int i = order.IndexOf(from);
        int j = order.IndexOf(to);
        return i < 0 || j < 0 ? [to] : order.Skip(Math.Min(i, j)).Take(Math.Abs(i - j) + 1);
    }

    /// <summary>
    /// Right clicking a row, column, or cell selects it.
    /// If it was already selected, it does nothing, so as not to clear the selection.
    /// Right clicking on empty space keeps the selection.
    /// </summary>
    private void SelectForMenu(Target target)
    {
        if (!MySelection().Contains(target))
            SelectTarget(target);
    }

    /// <summary>
    /// Highlights the selected rows, columns and cells in the colors <see cref="LocalSelection.SelectionColors"/> gives them.
    /// </summary>
    private void SyncSelection(IRecordReader R, SnowTag datasetId)
    {
        var colors = R.SelectionColors(s => s.Within == datasetId);
        foreach (var header in _headerCells)
            Highlight(header, "panel", HighlightOf(colors, Column(header.ColumnId)));
        foreach (var view in _views)
        {
            Highlight(view.Header, "panel", HighlightOf(colors, view.Target));
            for (int i = 0; i < view.Cells.Count; i++)
                Highlight(
                    view.Cells[i],
                    "normal",
                    HighlightOf(
                        colors,
                        new CellTarget(view.Row.Id, _columnIds[i]),
                        view.Target,
                        Column(_columnIds[i])
                    )
                );
        }
    }

    /// <summary>
    /// The selection color of the first of <paramref name="targets"/> anyone selected, like a cell before its row and column, or null.
    /// </summary>
    private static Color? HighlightOf(
        IReadOnlyDictionary<Target, Color> colors,
        params Target[] targets
    )
    {
        foreach (var target in targets)
            if (colors.TryGetValue(target, out var color))
                return color;
        return null;
    }

    /// <summary>
    /// Tints a control's theme style toward <paramref name="color"/>, keeping its padding and borders,
    /// or clears the tint with null.
    /// </summary>
    private void Highlight(Control control, string style, Color? color)
    {
        control.RemoveThemeStyleboxOverride(style);
        if (color is not Color c)
            return;

        // One tint per style and color: cells share "normal", and row and column headers share "panel".
        if (!_highlightStyles.TryGetValue((style, c), out var tinted))
        {
            if (control.GetThemeStylebox(style).Duplicate() is StyleBoxFlat flat)
            {
                flat.BgColor = flat.BgColor.Lerp(c, 0.35f);
                tinted = flat;
            }
            else
                tinted = new StyleBoxFlat { BgColor = c with { A = 0.35f } };
            _highlightStyles[(style, c)] = tinted;
        }
        control.AddThemeStyleboxOverride(style, tinted);
    }

    /// <summary>Runs <paramref name="command"/> on what the local player has selected, as its shortcut would.</summary>
    private void RunOnSelection(Command command)
    {
        var targets = MySelection().Where(t => command.Applies(RecordService.Instance, t)).ToList();
        if (command.Fits(targets.Count))
            command.Run(targets, 1, this);
    }

    /// <summary>
    /// The commands only the editor offers, since they act on its grid.
    /// </summary>
    private IReadOnlyList<Command> EditorCommands() =>
        [
            InsertRowAbove,
            InsertRowBelow,
            InsertColumnLeft,
            InsertColumnRight,
            new TargetCommand<ColumnTarget>
            {
                Name = new("dataset.rename_column"),
                Icon = UI.TextureUI_Pencil,
                Caption = "Rename Column",
                Count = TargetCount.One,
                SideEffects = (columns, _) => RenameColumn(columns[0]),
            },
        ];

    // The inserts only read records, so every editor shares them. The editor's listeners
    // focus the row they add or start renaming the column.

    private static readonly RecordCommand<DataRow> InsertRowAbove = new()
    {
        Name = new("dataset.insert_row_above"),
        Caption = "Insert Row Above",
        Count = TargetCount.One,
        Effects = (R, rows, _) =>
            DataSetCommands.NewRow(rows[0].DataSetId, R.RankBeside(rows[0], after: false)),
    };

    private static readonly RecordCommand<DataRow> InsertRowBelow = new()
    {
        Name = new("dataset.insert_row_below"),
        Caption = "Insert Row Below",
        Count = TargetCount.One,
        Effects = (R, rows, _) =>
            DataSetCommands.NewRow(rows[0].DataSetId, R.RankBeside(rows[0], after: true)),
    };

    private static readonly TargetCommand<ColumnTarget> InsertColumnLeft = new()
    {
        Name = new("dataset.insert_column_left"),
        Caption = "Insert Column Left",
        Count = TargetCount.One,
        AppliesTo = (R, t) => ColumnIndex(R, t) >= 0,
        Effects = (R, columns, _) =>
            DataSetCommands.NewColumn(
                R.Get<DataSet>(columns[0].DataSetId),
                ColumnIndex(R, columns[0])
            ),
    };

    private static readonly TargetCommand<ColumnTarget> InsertColumnRight = new()
    {
        Name = new("dataset.insert_column_right"),
        Caption = "Insert Column Right",
        Count = TargetCount.One,
        AppliesTo = (R, t) => ColumnIndex(R, t) >= 0,
        Effects = (R, columns, _) =>
            DataSetCommands.NewColumn(
                R.Get<DataSet>(columns[0].DataSetId),
                ColumnIndex(R, columns[0]) + 1
            ),
    };

    // The column's place in its dataset, or -1 if it's gone.
    private static int ColumnIndex(IRecordReader R, ColumnTarget t) =>
        R.Get<DataSet>(t.DataSetId)?.Columns.Select(c => c.Id).ToList().IndexOf(t.ColumnId) ?? -1;

    private void RenameColumn(ColumnTarget column)
    {
        // Deferred so the closing menu doesn't take focus back from the name box.
        if (_headerCells.FirstOrDefault(h => h.ColumnId == column.ColumnId) is { } header)
            Callable.From(header.BeginRename).CallDeferred();
    }

    private void OnImportPressed()
    {
        if (_currentDataSet == null)
            return;

        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Import CSV",
            Filters = new[] { "*.csv ; CSV Files", "*.txt ; Text Files" },
            Unresizable = false,
        };

        dialog.FileSelected += path =>
        {
            CsvImport.Replace(RecordService.Instance, _currentDataSet, path);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(700, 450));
    }

    private void OnColumnWidthDragged(SnowTag columnId, float width)
    {
        int index = _columnIds.IndexOf(columnId);
        if (index < 0)
            return;

        width = Mathf.Max(width, MinColumnWidth);
        _columnWidths[columnId] = width;

        _headerCells[index].CustomMinimumSize = new Vector2(width, HeaderHeight);
        foreach (var view in _views)
            view.Cells[index].CustomMinimumSize = new Vector2(width, RowHeight);
    }
}

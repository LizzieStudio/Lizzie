using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Godot;

public partial class DatasetEditor : Window
{
    /// <summary>One displayed row. <see cref="Row"/> is null for the blank row at the bottom.</summary>
    private sealed class RowView
    {
        public HBoxContainer Box;
        public Label Number;
        public CheckBox Check;
        public readonly List<LineEdit> Cells = new();
        public DataRow Row;
    }

    private SnowTag _datasetRef = SnowTag.Empty;
    private DataSet _currentDataSet;

    private VBoxContainer _mainContainer;
    private Button _deleteButton;
    private Button _newButton;
    private DataSetSelector _datasetList;
    private Button _linkButton;
    private Button _addColumnButton;
    private Button _deleteColumnButton;

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

    // A blank row the user is typing into. It's written when the cell loses focus,
    // but a new blank row already shows below it.
    private RowView _pending;
    private RowView _blank;

    private SnowTag _renameOnSync = SnowTag.Empty;
    private SnowTag _focusRowOnSync = SnowTag.Empty;
    private SnowTag _selectedColumn = SnowTag.Empty;
    private StyleBox _selectedCellStyle;

    // The pointer during a row or column drag, for auto-scrolling at the edges.
    private Vector2? _dragAt;
    private bool _dragIsRow;

    private PopupMenu _contextMenu;
    private readonly List<Action> _menuActions = new();

    private static readonly Color Accent = Color.FromHtml("8cb1ff");

    private const float RowHeaderWidth = 64f;
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

        CloseRequested += CloseDialog;
        MoveToCenter();
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    public override void _Process(double delta)
    {
        if (_dragAt is not Vector2 at)
            return;

        // Holding a drag near an edge scrolls toward it.
        var rect = _dataScrollContainer.GetGlobalRect();
        int step = (int)(AutoScrollSpeed * delta);
        if (_dragIsRow)
        {
            if (at.Y < rect.Position.Y + AutoScrollMargin)
                _dataScrollContainer.ScrollVertical -= step;
            else if (at.Y > rect.End.Y - AutoScrollMargin)
                _dataScrollContainer.ScrollVertical += step;
            ShowRowDrop(at);
        }
        else
        {
            if (at.X < rect.Position.X + AutoScrollMargin)
                _dataScrollContainer.ScrollHorizontal -= step;
            else if (at.X > rect.End.X - AutoScrollMargin)
                _dataScrollContainer.ScrollHorizontal += step;
            ShowColumnDrop(at);
        }
    }

    // Cells consume Delete while focused, so this only sees it when nothing is being edited.
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key)
            return;

        if (key.Keycode == Key.Delete)
        {
            if (_selectedColumn != SnowTag.Empty)
                DeleteColumn(_selectedColumn);
            else
                DeleteCheckedRows();
        }
        else if (key.Keycode == Key.Escape && _selectedColumn != SnowTag.Empty)
            SelectColumn(SnowTag.Empty);
        else
            return;

        SetInputAsHandled();
    }

    private void Sync(IRecordReader R)
    {
        var ds = R.Get<DataSet>(_datasetRef);
        if (ds == null)
        {
            ds = R.Get<DataSet>().FirstOrDefault();
            _datasetRef = ds?.Id ?? SnowTag.Empty;
        }

        _datasetList.SelectedDataSet = _datasetRef;
        _currentDataSet = ds;

        if (ds == null)
        {
            ClearGrid();
            return;
        }

        var columnIds = ds.Columns.Select(c => c.Id).ToList();
        if (ds.Id != _shownDataSetId || !columnIds.SequenceEqual(_columnIds))
        {
            ClearGrid();
            _shownDataSetId = ds.Id;
            _columnIds = columnIds;
            BuildGrid();
        }

        for (int i = 0; i < _headerCells.Count; i++)
            _headerCells[i].SetHeaderText(ds.Columns[i].Name);

        ReconcileRows(R.GetRows(ds.Id));

        if (!_columnIds.Contains(_selectedColumn))
            _selectedColumn = SnowTag.Empty;
        ApplyColumnSelection();

        if (_renameOnSync != SnowTag.Empty)
        {
            _headerCells.FirstOrDefault(h => h.ColumnId == _renameOnSync)?.BeginRename();
            _renameOnSync = SnowTag.Empty;
        }

        if (_focusRowOnSync != SnowTag.Empty)
        {
            FocusCell(_views.FirstOrDefault(v => v.Row.Id == _focusRowOnSync), 0);
            _focusRowOnSync = SnowTag.Empty;
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

        _deleteButton = GetNode<Button>("%DeleteRow");
        _deleteButton.Pressed += DeleteCheckedRows;

        _addColumnButton = GetNode<Button>("%AddColumn");
        _addColumnButton.Pressed += OnAddColumnPressed;

        _deleteColumnButton = GetNode<Button>("%DeleteColumn");
        _deleteColumnButton.Pressed += () => DeleteColumn(_selectedColumn);
        _deleteColumnButton.Disabled = true;

        _linkButton = GetNode<Button>("%Link");
        _linkButton.Pressed += OnImportPressed;

        _newButton = GetNode<Button>("%New");
        _newButton.Pressed += OnNewDatasetPressed;

        _datasetList = GetNode<DataSetSelector>("%DatasetList");
        _datasetList.DataSetSelected += OnDatasetSelected;

        InitializeNewDatasetDialog();

        _contextMenu = new PopupMenu();
        AddChild(_contextMenu);
        _contextMenu.IdPressed += id => _menuActions[(int)id]();
    }

    /// <summary>Opens the editor on a specific dataset. Empty opens the first one.</summary>
    public void SetDatasetById(SnowTag id)
    {
        _datasetRef = id;
        ProjectService.Instance.QueueSync(this);
    }

    private void OnDatasetSelected(SnowTag id) => SetDatasetById(id);

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
        if (_blank != null)
            OnBlankEdited(_blank);
        CommitPending();
        Closed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private float ColumnWidth(SnowTag id) =>
        _columnWidths.GetValueOrDefault(id, DefaultColumnWidth);

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
            header.Clicked += SelectColumn;
            header.ContextRequested += (columnId, at) =>
            {
                SelectColumn(columnId);
                ShowContextMenu(null, columnId, at);
            };
            header.ColumnDragMoved += (_, at) =>
            {
                _dragAt = at;
                _dragIsRow = false;
                ShowColumnDrop(at);
            };
            header.ColumnDropped += DropColumn;
            _headerCells.Add(header);
        }

        // Lets the header scroll as far as the data, whose view is narrowed by its vertical scrollbar.
        var spacer = new Control();
        _headerContainer.AddChild(spacer);
        spacer.CustomMinimumSize = new Vector2(ScrollbarAllowance, 0);

        _blank = CreateRowView();
    }

    private void ClearGrid()
    {
        // Forget the views first: freeing a focused pending cell fires FocusExited, which would write it.
        var views = AllRowViews();
        _views = new List<RowView>();
        _pending = null;
        _blank = null;
        foreach (var view in views)
            FreeRowView(view);

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

        var rowHeader = new HBoxContainer();
        view.Box.AddChild(rowHeader);
        rowHeader.CustomMinimumSize = new Vector2(RowHeaderWidth, RowHeight);
        rowHeader.MouseDefaultCursorShape = Control.CursorShape.Move;

        var drag = new DragGesture(rowHeader);
        drag.Moved += at =>
        {
            if (view.Row == null)
                return;
            _dragAt = at;
            _dragIsRow = true;
            ShowRowDrop(at);
        };
        drag.Dropped += at => DropRow(view, at);
        rowHeader.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } b)
            {
                ShowContextMenu(view, SnowTag.Empty, b.GlobalPosition);
                rowHeader.AcceptEvent();
            }
        };

        view.Number = new Label();
        rowHeader.AddChild(view.Number);
        view.Number.CustomMinimumSize = new Vector2(28, 0);
        view.Number.HorizontalAlignment = HorizontalAlignment.Right;
        view.Number.VerticalAlignment = VerticalAlignment.Center;
        view.Number.Modulate = new Color(1, 1, 1, 0.5f);

        view.Check = new CheckBox();
        rowHeader.AddChild(view.Check);
        view.Check.FocusMode = Control.FocusModeEnum.None;
        view.Check.Visible = false;
        view.Check.Toggled += _ =>
        {
            // Hand Delete to the editor, which deletes checked rows.
            SelectColumn(SnowTag.Empty);
            GuiReleaseFocus();
        };

        for (int i = 0; i < _columnIds.Count; i++)
        {
            var column = i;
            var cell = new LineEdit();
            view.Box.AddChild(cell);
            cell.CustomMinimumSize = new Vector2(ColumnWidth(_columnIds[i]), RowHeight);
            cell.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            cell.SizeFlagsVertical = Control.SizeFlags.Fill;
            cell.PlaceholderText = "Enter data...";
            cell.ContextMenuEnabled = false;

            StyleCell(cell, _columnIds[i]);

            cell.FocusEntered += () => OnCellFocused(cell);
            cell.FocusExited += () =>
            {
                // TextChanged arrives deferred, so the blank row may not know it has text yet.
                if (view == _blank)
                    OnBlankEdited(view);

                if (view == _pending)
                    CommitPending();
                else
                    CommitRow(view);
            };
            cell.TextChanged += _ =>
            {
                if (view.Row == null)
                    OnBlankEdited(view);
            };
            cell.GuiInput += e => OnCellInput(view, column, e);

            view.Cells.Add(cell);
        }

        return view;
    }

    private void FreeRowView(RowView view)
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
        view.Check.Visible = true;

        for (int i = 0; i < view.Cells.Count; i++)
        {
            var cell = view.Cells[i];
            cell.PlaceholderText = string.Empty;
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
        if (_pending != null)
            _dataContainer.MoveChild(_pending.Box, _dataContainer.GetChildCount() - 1);
        _dataContainer.MoveChild(_blank.Box, _dataContainer.GetChildCount() - 1);
    }

    private List<RowView> AllRowViews()
    {
        var all = new List<RowView>(_views);
        if (_pending != null)
            all.Add(_pending);
        if (_blank != null)
            all.Add(_blank);
        return all;
    }

    private RowView Neighbor(RowView view, int step)
    {
        var all = AllRowViews();
        int i = all.IndexOf(view) + step;
        return i >= 0 && i < all.Count ? all[i] : null;
    }

    private static void FocusCell(RowView view, int column)
    {
        if (view != null && column >= 0 && column < view.Cells.Count)
            view.Cells[column].GrabFocus();
    }

    private void OnCellFocused(LineEdit cell)
    {
        SelectColumn(SnowTag.Empty);

        // Keyboard navigation selects the whole cell, like a spreadsheet; a click places the caret.
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
            cell.CallDeferred(LineEdit.MethodName.SelectAll);
    }

    private void OnCellInput(RowView view, int column, InputEvent e)
    {
        var cell = view.Cells[column];

        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click)
        {
            ShowContextMenu(view, _columnIds[column], click.GlobalPosition);
            cell.AcceptEvent();
            return;
        }

        if (e is not InputEventKey { Pressed: true } key)
            return;

        // Multi-cell clipboard text spreads across cells; anything else pastes as usual.
        if (e.IsActionPressed("ui_paste"))
        {
            var text = DisplayServer.ClipboardGet().TrimEnd('\r', '\n');
            if (text.Contains('\t') || text.Contains('\n'))
            {
                PasteGrid(view, column, ParseTsv(text));
                cell.AcceptEvent();
            }
            return;
        }

        // TextChanged arrives deferred, so catch up before a key moves away from the blank row.
        if (view.Row == null)
            OnBlankEdited(view);

        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                if (view == _pending)
                    CommitPending();
                else
                    CommitRow(view);
                FocusCell(Neighbor(view, key.ShiftPressed ? -1 : 1), 0);
                break;
            case Key.Up:
                FocusCell(Neighbor(view, -1), column);
                break;
            case Key.Down:
                FocusCell(Neighbor(view, 1), column);
                break;
            case Key.Escape:
                cell.Text =
                    view.Row?.Data.GetValueOrDefault(_columnIds[column], string.Empty)
                    ?? string.Empty;
                cell.SelectAll();
                if (view.Row == null)
                    OnBlankEdited(view);
                break;
            default:
                return;
        }
        cell.AcceptEvent();
    }

    /// <summary>Applies the row's cells to <paramref name="data"/>; empty cells remove their key.</summary>
    private ImmutableDictionary<SnowTag, string> ApplyCells(
        RowView view,
        ImmutableDictionary<SnowTag, string> data
    )
    {
        for (int i = 0; i < view.Cells.Count; i++)
        {
            var text = view.Cells[i].Text;
            data =
                text.Length == 0 ? data.Remove(_columnIds[i]) : data.SetItem(_columnIds[i], text);
        }
        return data;
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

    private void CommitRow(RowView view)
    {
        if (view.Row == null || _currentDataSet == null)
            return;

        var data = ApplyCells(view, view.Row.Data);
        if (DataEqual(data, view.Row.Data))
            return;

        view.Row = view.Row with { Data = data };
        ProjectService.Instance.Upsert(view.Row);
    }

    private static void SetPlaceholders(RowView view, bool shown)
    {
        foreach (var cell in view.Cells)
            cell.PlaceholderText = shown ? "Enter data..." : string.Empty;
    }

    /// <summary>
    /// Typing into the blank row makes it pending and shows a new blank row below it;
    /// emptying the pending row turns it back into the blank row. Nothing is written until a cell loses focus.
    /// </summary>
    private void OnBlankEdited(RowView view)
    {
        bool empty = ApplyCells(view, ImmutableDictionary<SnowTag, string>.Empty).IsEmpty;
        if (view == _blank && !empty)
        {
            _pending = _blank;
            SetPlaceholders(_pending, false);
            _blank = CreateRowView();
        }
        else if (view == _pending && empty && !_blank.Cells.Any(c => c.HasFocus()))
        {
            FreeRowView(_blank);
            _blank = _pending;
            _pending = null;
            SetPlaceholders(_blank, true);
        }
    }

    /// <summary>Writes the pending row as a real row.</summary>
    private void CommitPending()
    {
        var view = _pending;
        if (view == null || _currentDataSet == null)
            return;

        var data = ApplyCells(view, ImmutableDictionary<SnowTag, string>.Empty);
        if (data.IsEmpty)
            return;

        var row = new DataRow
        {
            Id = Snowport.Clock.CreateTag(),
            DataSetId = _currentDataSet.Id,
            Rank = RowRank.Between(_views.LastOrDefault()?.Row.Rank, null),
            Data = data,
        };
        ProjectService.Instance.Upsert(row);

        _pending = null;
        _views.Add(view);
        BindRowView(view, row, _views.Count - 1);
    }

    private void ShowDropIndicator(Rect2 rect)
    {
        _dropIndicator.Position = rect.Position;
        _dropIndicator.Size = rect.Size;
        _dropIndicator.Visible = true;
    }

    private int RowDropIndex(Vector2 at) =>
        _views.Count(v => v.Box.GetGlobalRect().GetCenter().Y < at.Y);

    private void ShowRowDrop(Vector2 at)
    {
        if (_views.Count == 0)
            return;

        int index = RowDropIndex(at);
        float y =
            index < _views.Count
                ? _views[index].Box.GetGlobalRect().Position.Y
                : _views[^1].Box.GetGlobalRect().End.Y;

        var clip = _dataScrollContainer.GetGlobalRect();
        y = Mathf.Clamp(y, clip.Position.Y, clip.End.Y);
        ShowDropIndicator(
            new Rect2(clip.Position.X, y - IndicatorThickness / 2, clip.Size.X, IndicatorThickness)
        );
    }

    private void DropRow(RowView view, Vector2 at)
    {
        _dropIndicator.Visible = false;
        _dragAt = null;

        int from = _views.IndexOf(view);
        if (from < 0)
            return;

        int to = RowDropIndex(at);
        if (to == from || to == from + 1)
            return;

        PlaceRow(view.Row, to > from ? to - 1 : to);
    }

    /// <summary>Writes <paramref name="row"/> so it sits at <paramref name="index"/> among the other rows.</summary>
    private void PlaceRow(DataRow row, int index)
    {
        var others = _views.Select(v => v.Row).Where(r => r.Id != row.Id).ToList();
        var prev = index > 0 ? others[index - 1] : null;
        var next = index < others.Count ? others[index] : null;

        var batch = new UpsertBatch();
        if (prev == null || next == null || RowRank.Comparer.Compare(prev.Rank, next.Rank) < 0)
        {
            batch.Add(row with { Rank = RowRank.Between(prev?.Rank, next?.Rank) });
        }
        else
        {
            // Tied ranks leave no room between them, so rank every row in the new order.
            others.Insert(index, row);
            string rank = null;
            foreach (var r in others)
            {
                rank = RowRank.Between(rank, null);
                batch.Add(r with { Rank = rank });
            }
        }
        batch.Submit();
    }

    private int ColumnDropIndex(Vector2 at) =>
        _headerCells.Count(h => h.GetGlobalRect().GetCenter().X < at.X);

    private void ShowColumnDrop(Vector2 at)
    {
        if (_headerCells.Count == 0)
            return;

        int index = ColumnDropIndex(at);
        float x =
            index < _headerCells.Count
                ? _headerCells[index].GetGlobalRect().Position.X
                : _headerCells[^1].GetGlobalRect().End.X;

        var header = _headerScroll.GetGlobalRect();
        var data = _dataScrollContainer.GetGlobalRect();
        x = Mathf.Clamp(x, header.Position.X, header.End.X);
        ShowDropIndicator(
            new Rect2(
                x - IndicatorThickness / 2,
                header.Position.Y,
                IndicatorThickness,
                data.End.Y - header.Position.Y
            )
        );
    }

    private void DropColumn(SnowTag columnId, Vector2 at)
    {
        _dropIndicator.Visible = false;
        _dragAt = null;

        if (_currentDataSet == null)
            return;

        var columns = _currentDataSet.Columns;
        int from = columns.IndexOf(columns.FirstOrDefault(c => c.Id == columnId));
        if (from < 0)
            return;

        int to = ColumnDropIndex(at);
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

    private void OnAddColumnPressed() => InsertColumn(_currentDataSet?.Columns.Length ?? 0);

    private void InsertColumn(int index)
    {
        if (_currentDataSet == null)
            return;

        var column = new Column
        {
            Id = Snowport.Clock.CreateTag(),
            Name = $"Column {_currentDataSet.Columns.Length + 1}",
        };
        _currentDataSet = _currentDataSet with
        {
            Columns = _currentDataSet.Columns.Insert(index, column),
        };
        ProjectService.Instance.Upsert(_currentDataSet);

        // Name it right away once its header exists.
        _renameOnSync = column.Id;
    }

    private void SelectColumn(SnowTag columnId)
    {
        if (columnId == _selectedColumn)
            return;

        _selectedColumn = columnId;
        ApplyColumnSelection();

        // Hand Delete to the editor instead of the cell being edited.
        if (columnId != SnowTag.Empty)
            GuiReleaseFocus();
    }

    private void ApplyColumnSelection()
    {
        foreach (var header in _headerCells)
            header.SetSelected(header.ColumnId == _selectedColumn);

        foreach (var view in AllRowViews())
            for (int i = 0; i < view.Cells.Count; i++)
                StyleCell(view.Cells[i], _columnIds[i]);

        _deleteColumnButton.Disabled = _selectedColumn == SnowTag.Empty;
    }

    private void StyleCell(LineEdit cell, SnowTag columnId)
    {
        if (columnId != _selectedColumn)
        {
            cell.RemoveThemeStyleboxOverride("normal");
            return;
        }

        if (_selectedCellStyle == null)
        {
            // Tint the theme's own cell style so padding and borders stay the same.
            _selectedCellStyle = (StyleBox)cell.GetThemeStylebox("normal").Duplicate();
            if (_selectedCellStyle is StyleBoxFlat flat)
                flat.BgColor = flat.BgColor.Lerp(Accent, 0.35f);
            else
                _selectedCellStyle = new StyleBoxFlat { BgColor = Accent with { A = 0.35f } };
        }
        cell.AddThemeStyleboxOverride("normal", _selectedCellStyle);
    }

    private void DeleteColumn(SnowTag columnId)
    {
        if (_currentDataSet == null || columnId == SnowTag.Empty)
            return;

        _currentDataSet = _currentDataSet with
        {
            Columns = _currentDataSet.Columns.RemoveAll(c => c.Id == columnId),
        };
        if (_selectedColumn == columnId)
            _selectedColumn = SnowTag.Empty;
        ProjectService.Instance.Upsert(_currentDataSet);
    }

    private void DeleteCheckedRows() => DeleteRows(_views.Where(v => v.Check.ButtonPressed));

    private void DeleteRows(IEnumerable<RowView> views)
    {
        if (_currentDataSet == null)
            return;

        var batch = new UpsertBatch();
        foreach (var view in views)
            batch.Add(view.Row with { Deleted = true });
        batch.Submit();
    }

    private void InsertRow(int index)
    {
        if (_currentDataSet == null)
            return;

        var row = new DataRow { Id = Snowport.Clock.CreateTag(), DataSetId = _currentDataSet.Id };
        PlaceRow(row, index);
        _focusRowOnSync = row.Id;
    }

    private void AddMenuItem(string label, Action action, bool enabled = true)
    {
        _contextMenu.AddItem(label, _menuActions.Count);
        _contextMenu.SetItemDisabled(_contextMenu.ItemCount - 1, !enabled);
        _menuActions.Add(action);
    }

    /// <summary>
    /// Shows the menu for a row, a column, or a cell (both). <paramref name="at"/> is in this window's coordinates.
    /// </summary>
    private void ShowContextMenu(RowView view, SnowTag columnId, Vector2 at)
    {
        _contextMenu.Clear();
        _menuActions.Clear();

        int column = _columnIds.IndexOf(columnId);
        if (view != null && column >= 0)
        {
            var cell = view.Cells[column];
            AddMenuItem(
                "Cut",
                () =>
                {
                    DisplayServer.ClipboardSet(cell.Text);
                    PasteGrid(
                        view,
                        column,
                        [
                            [string.Empty],
                        ]
                    );
                }
            );
            AddMenuItem("Copy", () => DisplayServer.ClipboardSet(cell.Text));
            AddMenuItem(
                "Paste",
                () =>
                    PasteGrid(
                        view,
                        column,
                        ParseTsv(DisplayServer.ClipboardGet().TrimEnd('\r', '\n'))
                    )
            );
            _contextMenu.AddSeparator();
        }

        if (view != null)
        {
            int index = _views.IndexOf(view);
            bool real = index >= 0;
            // Act on every checked row when the clicked row is one of them.
            List<RowView> targets =
                real && view.Check.ButtonPressed
                    ? _views.Where(v => v.Check.ButtonPressed).ToList()
                    : [view];

            AddMenuItem("Insert Row Above", () => InsertRow(index), real);
            AddMenuItem("Insert Row Below", () => InsertRow(index + 1), real);
            AddMenuItem(
                targets.Count > 1 ? $"Delete {targets.Count} Rows" : "Delete Row",
                () => DeleteRows(targets),
                real
            );
        }

        if (column >= 0)
        {
            if (view != null)
                _contextMenu.AddSeparator();

            AddMenuItem("Insert Column Left", () => InsertColumn(column));
            AddMenuItem("Insert Column Right", () => InsertColumn(column + 1));
            if (view == null)
            {
                // Deferred so the closing menu doesn't take focus back from the name box.
                var header = _headerCells[column];
                AddMenuItem(
                    "Rename Column",
                    () => Callable.From(header.BeginRename).CallDeferred()
                );
            }
            AddMenuItem("Delete Column", () => DeleteColumn(columnId));
        }

        _contextMenu.ResetSize();
        _contextMenu.Popup(new Rect2I(Position + (Vector2I)at, Vector2I.Zero));
    }

    /// <summary>
    /// Writes a block of values into the grid, starting at a cell and filling rightward and downward.
    /// Values past the last column are dropped; rows past the end become new rows. One event, one undo.
    /// </summary>
    private void PasteGrid(RowView start, int column, List<string[]> grid)
    {
        if (_currentDataSet == null || grid.Count == 0)
            return;

        var targets = AllRowViews();
        int first = targets.IndexOf(start);
        string rank = _views.LastOrDefault()?.Row.Rank;
        var batch = new UpsertBatch();

        for (int r = 0; r < grid.Count; r++)
        {
            var view = first + r < targets.Count ? targets[first + r] : null;
            var data = ImmutableDictionary<SnowTag, string>.Empty;
            if (view?.Row != null)
                data = view.Row.Data;
            else if (view != null)
                data = ApplyCells(view, data);

            for (int c = 0; c < grid[r].Length && column + c < _columnIds.Count; c++)
            {
                var id = _columnIds[column + c];
                data = grid[r][c].Length == 0 ? data.Remove(id) : data.SetItem(id, grid[r][c]);
            }

            DataRow row;
            if (view?.Row != null)
                row = view.Row with { Data = data };
            else
            {
                rank = RowRank.Between(rank, null);
                row = new DataRow
                {
                    Id = Snowport.Clock.CreateTag(),
                    DataSetId = _currentDataSet.Id,
                    Rank = rank,
                    Data = data,
                };
            }
            batch.Add(row);

            // Rows past the existing views get theirs on the next sync.
            if (view == null)
                continue;

            if (view.Row == null)
            {
                // The pending or blank row becomes a real row.
                if (view == _pending)
                    _pending = null;
                if (view == _blank)
                    _blank = null;
                _views.Add(view);
            }

            // Set every cell, including a focused one, which BindRowView leaves alone.
            for (int i = 0; i < view.Cells.Count; i++)
                view.Cells[i].Text = data.GetValueOrDefault(_columnIds[i], string.Empty);
            BindRowView(view, row, _views.IndexOf(view));
        }

        batch.Submit();

        if (_blank == null)
            _blank = CreateRowView();
    }

    /// <summary>
    /// Parses tab-separated text as spreadsheets copy it: tabs between cells, newlines between rows,
    /// and quotes around cells that contain either (with "" for a literal quote).
    /// </summary>
    private static List<string[]> ParseTsv(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch != '"')
                    cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    cell.Append(text[++i]);
                else
                    quoted = false;
            }
            else if (ch == '"' && cell.Length == 0)
                quoted = true;
            else if (ch == '\t')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(cell.ToString());
                rows.Add(row.ToArray());
                row.Clear();
                cell.Clear();
            }
            else
                cell.Append(ch);
        }

        row.Add(cell.ToString());
        rows.Add(row.ToArray());
        return rows;
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
            ImportCsv(path);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(700, 450));
    }

    private void ImportCsv(string path)
    {
        if (_currentDataSet == null)
            return;

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"Could not open CSV file '{path}': {FileAccess.GetOpenError()}");
            return;
        }

        var lines = new List<string[]>();
        while (!file.EofReached())
        {
            var cells = file.GetCsvLine();
            if (cells.Length == 1 && string.IsNullOrEmpty(cells[0]))
                continue;
            lines.Add(cells);
        }

        if (lines.Count == 0)
        {
            GD.PrintErr($"CSV file '{path}' contained no data.");
            return;
        }

        var header = lines[0];

        var batch = new UpsertBatch();

        var currentColumns = _currentDataSet.Columns.Select(c => c.Name);

        if (!currentColumns.SequenceEqual(header))
        {
            // TODO: we should do column matching based on string
            var nextColumns = header
                .Select(h => new Column { Id = Snowport.Clock.CreateTag(), Name = h })
                .ToImmutableArray();
            _currentDataSet = _currentDataSet with { Columns = nextColumns };
            batch.Add(_currentDataSet);
        }

        foreach (var existing in ProjectService.Instance.GetRows(_currentDataSet.Id))
            batch.Add(existing with { Deleted = true });

        string prevRank = null;
        for (int i = 1; i < lines.Count; i++)
        {
            var data = new Dictionary<SnowTag, string>();
            for (int c = 0; c < _currentDataSet.Columns.Length && c < lines[i].Length; c++)
            {
                if (!string.IsNullOrEmpty(lines[i][c]))
                    data[_currentDataSet.Columns[c].Id] = lines[i][c];
            }

            prevRank = RowRank.Between(prevRank, null);
            batch.Add(
                new DataRow
                {
                    Id = Snowport.Clock.CreateTag(),
                    DataSetId = _currentDataSet.Id,
                    Rank = prevRank,
                    Data = data.ToImmutableDictionary(),
                }
            );
        }

        batch.Submit();
    }

    private void OnColumnWidthDragged(SnowTag columnId, float width)
    {
        int index = _columnIds.IndexOf(columnId);
        if (index < 0)
            return;

        width = Mathf.Max(width, MinColumnWidth);
        _columnWidths[columnId] = width;

        _headerCells[index].CustomMinimumSize = new Vector2(width, HeaderHeight);
        foreach (var view in AllRowViews())
            view.Cells[index].CustomMinimumSize = new Vector2(width, RowHeight);
    }
}

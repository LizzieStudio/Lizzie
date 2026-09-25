using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

public partial class DatasetEditor : Window
{
    private SnowTag _datasetRef = SnowTag.Empty;
    private DataSet _currentDataSet;

    private VBoxContainer _mainContainer;
    private Button _deleteButton;
    private Button _newButton;
    private DataSetSelector _datasetList;
    private Button _linkButton;
    private Button _addColumnButton;
    private Button _deleteColumnButton;

    private HBoxContainer _headerContainer;
    private ScrollContainer _dataScrollContainer;
    private VBoxContainer _dataContainer;

    private List<float> _columnWidths = new();
    private List<CheckBox> _rowCheckboxes = new();

    private ConfirmationDialog _newDatasetDialog;
    private LineEdit _newDatasetNameInput;
    private Label _newDatasetErrorLabel;
    private HBoxContainer _newRowContainer;

    private IReadOnlyList<DataRow> _rows = new List<DataRow>();

    private List<SnowTag> _columnIds = new();

    private const float CheckboxColumnWidth = 40f;
    private const float DefaultColumnWidth = 120f;
    private const float MinColumnWidth = 50f;
    private const float HeaderHeight = 30f;
    private const float RowHeight = 30f;

    public override void _Ready()
    {
        InitializeSpreadsheet();

        CloseRequested += CloseDialog;
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
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
            _rows = new List<DataRow>();
            ClearSpreadsheet();
            return;
        }

        _rows = R.GetRows(ds.Id);
        RebuildGrid();
    }

    private void InitializeSpreadsheet()
    {
        _mainContainer = GetNode<VBoxContainer>("%MainContainer");
        _mainContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _headerContainer = new HBoxContainer();
        _headerContainer.CustomMinimumSize = new Vector2(0, HeaderHeight);
        _mainContainer.AddChild(_headerContainer);

        _dataScrollContainer = new ScrollContainer();
        _dataScrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _dataScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
        _dataScrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _mainContainer.AddChild(_dataScrollContainer);

        _dataContainer = new VBoxContainer();
        _dataScrollContainer.AddChild(_dataContainer);

        _deleteButton = GetNode<Button>("%DeleteRow");
        _deleteButton.Pressed += OnDeleteButtonPressed;

        _addColumnButton = GetNode<Button>("%AddColumn");
        _addColumnButton.Pressed += OnAddColumnPressed;

        _deleteColumnButton = GetNode<Button>("%DeleteColumn");
        _deleteColumnButton.Pressed += OnDeleteColumnPressed;

        _linkButton = GetNode<Button>("%Link");
        _linkButton.Pressed += OnImportPressed;

        _newButton = GetNode<Button>("%New");
        _newButton.Pressed += OnNewDatasetPressed;

        _datasetList = GetNode<DataSetSelector>("%DatasetList");
        _datasetList.DataSetSelected += OnDatasetSelected;

        InitializeNewDatasetDialog();
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
        Closed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void RebuildGrid()
    {
        if (_mainContainer == null || _currentDataSet == null)
            return;

        ClearSpreadsheet();

        _columnIds = _currentDataSet.Columns.Select(c => c.Id).ToList();

        _columnWidths.Clear();
        _rowCheckboxes.Clear();
        for (int i = 0; i < _currentDataSet.Columns.Length; i++)
            _columnWidths.Add(DefaultColumnWidth);

        CreateHeaderRow();
        CreateDataRows();
        CreateNewRow();
    }

    private void ClearSpreadsheet()
    {
        foreach (var c in _headerContainer.GetChildren())
            c.QueueFree();

        foreach (var c in _dataContainer.GetChildren())
            c.QueueFree();
    }

    private void CreateHeaderRow()
    {
        // Add checkbox column header
        var checkboxHeader = new PanelContainer();
        checkboxHeader.CustomMinimumSize = new Vector2(CheckboxColumnWidth, HeaderHeight);
        _headerContainer.AddChild(checkboxHeader);

        for (int i = 0; i < _currentDataSet.Columns.Length; i++)
        {
            var headerCell = new HeaderCell();
            headerCell.SetColumnIndex(i);
            headerCell.SetHeaderText(_currentDataSet.Columns[i].Name);
            headerCell.CustomMinimumSize = new Vector2(_columnWidths[i], HeaderHeight);
            headerCell.ColumnResized += OnColumnResized;

            _headerContainer.AddChild(headerCell);
        }
    }

    private void CreateDataRows()
    {
        foreach (var row in _rows)
        {
            var rowContainer = new HBoxContainer();
            rowContainer.CustomMinimumSize = new Vector2(0, RowHeight);

            // Add checkbox as first cell
            var checkboxCell = new CenterContainer();
            checkboxCell.CustomMinimumSize = new Vector2(CheckboxColumnWidth, RowHeight);
            var checkbox = new CheckBox();
            _rowCheckboxes.Add(checkbox);
            checkboxCell.AddChild(checkbox);
            rowContainer.AddChild(checkboxCell);

            for (int i = 0; i < _columnIds.Count; i++)
            {
                var cell = new LineEdit();
                cell.Text = row.Data.GetValueOrDefault(_columnIds[i], string.Empty);
                cell.CustomMinimumSize = new Vector2(_columnWidths[i], RowHeight);
                cell.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                cell.SizeFlagsVertical = Control.SizeFlags.Fill;

                var captured = row;
                var container = rowContainer;
                cell.FocusExited += () => CommitRow(captured, container);
                cell.TextSubmitted += _ => CommitRow(captured, container);

                rowContainer.AddChild(cell);
            }

            _dataContainer.AddChild(rowContainer);
        }
    }

    private void CreateNewRow()
    {
        _newRowContainer = new HBoxContainer();
        _newRowContainer.CustomMinimumSize = new Vector2(0, RowHeight);

        // Add empty checkbox cell
        var checkboxCell = new CenterContainer();
        checkboxCell.CustomMinimumSize = new Vector2(CheckboxColumnWidth, RowHeight);
        _newRowContainer.AddChild(checkboxCell);

        for (int i = 0; i < _columnIds.Count; i++)
        {
            var cell = new LineEdit();
            cell.CustomMinimumSize = new Vector2(_columnWidths[i], RowHeight);
            cell.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            cell.SizeFlagsVertical = Control.SizeFlags.Fill;
            cell.PlaceholderText = "Enter data...";

            cell.FocusExited += () => Callable.From(TryCommitNewRow).CallDeferred();
            cell.TextSubmitted += _ => CommitNewRow();

            _newRowContainer.AddChild(cell);
        }

        _dataContainer.AddChild(_newRowContainer);
    }

    private Dictionary<SnowTag, string> BuildRowData(HBoxContainer rowContainer)
    {
        var data = new Dictionary<SnowTag, string>();
        for (int i = 1; i < rowContainer.GetChildCount(); i++) // skip checkbox cell
        {
            if (rowContainer.GetChild(i) is not LineEdit cell)
                continue;
            int col = i - 1;
            if (col < _columnIds.Count && !string.IsNullOrEmpty(cell.Text))
                data[_columnIds[col]] = cell.Text;
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

    private void CommitRow(DataRow snapshot, HBoxContainer rowContainer)
    {
        if (_currentDataSet == null || !IsInstanceValid(rowContainer))
            return;

        var newData = BuildRowData(rowContainer);
        if (DataEqual(newData, snapshot.Data))
            return;

        ProjectService.Instance.Upsert(
            new DataRow
            {
                Id = snapshot.Id,
                DataSetId = snapshot.DataSetId,
                Rank = snapshot.Rank,
                Data = newData.ToImmutableDictionary(),
            }
        );
    }

    private void TryCommitNewRow()
    {
        if (_newRowContainer == null || !IsInstanceValid(_newRowContainer))
            return;

        var focus = GetViewport()?.GuiGetFocusOwner();
        if (focus != null && _newRowContainer.IsAncestorOf(focus))
            return;

        CommitNewRow();
    }

    private void CommitNewRow()
    {
        if (_currentDataSet == null || _newRowContainer == null)
            return;

        var newData = BuildRowData(_newRowContainer);
        if (newData.Count == 0)
            return;

        var lastRank = _rows.Count > 0 ? _rows[^1].Rank : null;
        var row = new DataRow
        {
            Id = Snowport.Clock.CreateTag(),
            DataSetId = _currentDataSet.Id,
            Rank = RowRank.Between(lastRank, null),
            Data = newData.ToImmutableDictionary(),
        };

        ProjectService.Instance.Upsert(row);
    }

    private void OnAddColumnPressed()
    {
        if (_currentDataSet == null)
            return;

        _currentDataSet = _currentDataSet with
        {
            Columns = _currentDataSet.Columns.Add(
                new Column
                {
                    Id = Snowport.Clock.CreateTag(),
                    Name = $"Column {_currentDataSet.Columns.Length + 1}",
                }
            ),
        };
        ProjectService.Instance.Upsert(_currentDataSet);
    }

    private void OnDeleteColumnPressed()
    {
        if (_currentDataSet == null || _currentDataSet.Columns.Length == 0)
            return;

        _currentDataSet = _currentDataSet with
        {
            Columns = _currentDataSet.Columns.RemoveAt(_currentDataSet.Columns.Length - 1),
        };
        ProjectService.Instance.Upsert(_currentDataSet);
    }

    private void OnDeleteButtonPressed()
    {
        if (_currentDataSet == null)
            return;

        var batch = new UpsertBatch();
        for (int i = 0; i < _rows.Count && i < _rowCheckboxes.Count; i++)
        {
            if (_rowCheckboxes[i].ButtonPressed)
                batch.Add(_rows[i] with { Deleted = true });
        }
        batch.Submit();
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

    private void OnColumnResized(int columnIndex, float newWidth)
    {
        if (columnIndex < 0 || columnIndex >= _columnWidths.Count)
            return;

        _columnWidths[columnIndex] = Mathf.Max(newWidth, MinColumnWidth);

        UpdateColumnWidth(columnIndex);
    }

    private void UpdateColumnWidth(int columnIndex)
    {
        var headerChildren = _headerContainer.GetChildren();
        if (columnIndex + 1 < headerChildren.Count)
        {
            var header = headerChildren[columnIndex + 1] as HeaderCell;
            if (header != null)
            {
                header.CustomMinimumSize = new Vector2(_columnWidths[columnIndex], HeaderHeight);
            }
        }

        foreach (var row in _dataContainer.GetChildren())
        {
            if (row is HBoxContainer rowContainer)
            {
                var cells = rowContainer.GetChildren();
                // Skip the first child (checkbox cell)
                if (columnIndex + 1 < cells.Count && cells[columnIndex + 1] is LineEdit cell)
                {
                    cell.CustomMinimumSize = new Vector2(_columnWidths[columnIndex], RowHeight);
                }
            }
        }
    }
}

public partial class HeaderCell : PanelContainer
{
    private Label _label;
    private Panel _resizeHandle;
    private bool _isResizing;
    private float _resizeStartX;
    private float _resizeStartWidth;
    private int _columnIndex;

    private string _headerText;

    [Signal]
    public delegate void ColumnResizedEventHandler(int columnIndex, float newWidth);

    public override void _Ready()
    {
        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(hbox);

        _label = new Label();
        _label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.Text = _headerText;
        _label.AddThemeColorOverride("font_color", Color.FromHtml("8cb1ff"));
        hbox.AddChild(_label);

        _resizeHandle = new Panel();
        _resizeHandle.CustomMinimumSize = new Vector2(8, 0);
        _resizeHandle.MouseDefaultCursorShape = CursorShape.Hsize;
        _resizeHandle.SizeFlagsVertical = SizeFlags.ExpandFill;
        hbox.AddChild(_resizeHandle);

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        _resizeHandle.AddThemeStyleboxOverride("panel", styleBox);

        _resizeHandle.GuiInput += OnResizeHandleInput;
    }

    public void SetHeaderText(string text)
    {
        _headerText = text;
        if (_label != null)
            _label.Text = text;
    }

    public string GetHeaderText()
    {
        return _headerText ?? string.Empty;
    }

    public void SetColumnIndex(int index)
    {
        _columnIndex = index;
    }

    private void OnResizeHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _isResizing = true;
                    _resizeStartX = mouseButton.GlobalPosition.X;
                    _resizeStartWidth = CustomMinimumSize.X;
                }
                else
                {
                    _isResizing = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isResizing)
        {
            float delta = mouseMotion.GlobalPosition.X - _resizeStartX;
            float newWidth = _resizeStartWidth + delta;

            EmitSignal(SignalName.ColumnResized, _columnIndex, newWidth);
        }
    }
}

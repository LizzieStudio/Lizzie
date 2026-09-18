using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class DatasetEditor : Window
{
    private Project _project;
    private DataSet _currentDataSet;

    private VBoxContainer _mainContainer;
    private Button _deleteButton;
    private Button _saveButton;
    private Button _cancelButton;
    private Button _newButton;
    private OptionButton _datasetList;
    private SnowTag _pendingDatasetRef = SnowTag.Empty;
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
    private int _nextRowId = 0;
    private const float CheckboxColumnWidth = 40f;
    private const float DefaultColumnWidth = 120f;
    private const float MinColumnWidth = 50f;
    private const float HeaderHeight = 30f;
    private const float RowHeight = 30f;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _project = ProjectService.Instance.CurrentProject;

        InitializeSpreadsheet();
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

        _saveButton = GetNode<Button>("%Save");
        _saveButton.Pressed += SaveDataSet;
        _cancelButton = GetNode<Button>("%Cancel");
        _cancelButton.Pressed += CloseDialog;
        _newButton = GetNode<Button>("%New");
        _newButton.Pressed += OnNewDatasetPressed;

        _datasetList = GetNode<OptionButton>("%DatasetList");
        _datasetList.ItemSelected += OnDatasetSelected;

        InitializeNewDatasetDialog();
        LoadDatasetList();

        if (_pendingDatasetRef != SnowTag.Empty)
            SelectDatasetById(_pendingDatasetRef);
    }

    /// <summary>Opens the editor on a specific dataset once the node is ready.</summary>
    public void SetDatasetById(SnowTag id)
    {
        if (id == SnowTag.Empty)
            return;
        if (!IsNodeReady())
        {
            _pendingDatasetRef = id;
            return;
        }
        SelectDatasetById(id);
    }

    private void SelectDatasetById(SnowTag id)
    {
        var idx = _datasetList.GetItemIndex(id.Value);
        if (idx < 0)
            return;

        _datasetList.Select(idx);
        var ds = ProjectService.Instance.GetDataSet(id);
        if (ds != null)
            MapDataSet(ds);
    }

    private void LoadDatasetList()
    {
        if (_project == null || _datasetList == null)
            return;

        _datasetList.Clear();
        foreach (var d in _project.Datasets.Values.Where(v => !v.Deleted))
        {
            _datasetList.AddItem(d.Name, d.Id.Value);
        }

        if (_datasetList.ItemCount > 0)
        {
            _datasetList.Select(0);
            MapDataSet(ProjectService.Instance.GetDataSet(new SnowTag(_datasetList.GetItemId(0))));
        }
    }

    private void OnDatasetSelected(long index)
    {
        if (_project == null || index < 0)
            return;

        var ds = ProjectService.Instance.GetDataSet(
            new SnowTag(_datasetList.GetItemId((int)index))
        );
        if (ds != null)
            MapDataSet(ds);
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
        if (_project == null)
            return;

        var name = _newDatasetNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var ds = new DataSet { Name = name };
        ProjectService.Instance.UpdateDataSet(ds);

        LoadDatasetList();
        SelectDatasetById(ds.Id);
    }

    public event EventHandler Closed;

    private void CloseDialog()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void SaveDataSet()
    {
        if (_currentDataSet == null)
            return;

        CommitGridToDataSet();

        ProjectService.Instance.UpdateDataSet(_currentDataSet);

        GD.Print("Dataset saved successfully");

        CloseDialog();
    }

    private void CommitGridToDataSet()
    {
        if (_currentDataSet == null)
            return;

        // Update column headers from HeaderCell controls
        _currentDataSet.Columns.Clear();
        var headerChildren = _headerContainer.GetChildren();
        for (int i = 1; i < headerChildren.Count; i++) // Skip first child (checkbox header)
        {
            if (headerChildren[i] is HeaderCell headerCell)
            {
                _currentDataSet.Columns.Add(headerCell.GetHeaderText());
            }
        }

        // Update row data from LineEdit controls
        var rowContainers = _dataContainer.GetChildren();
        int rowIndex = 0;

        foreach (var child in rowContainers)
        {
            if (child is HBoxContainer rowContainer)
            {
                // Skip the new row container (last one)
                if (rowContainer == _newRowContainer)
                {
                    // Check if new row has data and add it
                    bool hasData = false;
                    var rowData = new List<string>();

                    for (int i = 1; i < rowContainer.GetChildCount(); i++) // Skip checkbox cell
                    {
                        if (rowContainer.GetChild(i) is LineEdit cell)
                        {
                            rowData.Add(cell.Text);
                            if (!string.IsNullOrWhiteSpace(cell.Text))
                            {
                                hasData = true;
                            }
                        }
                    }

                    if (hasData)
                    {
                        var rowKey = _nextRowId.ToString();
                        _nextRowId++;
                        var newRow = new DataRow { Data = rowData };
                        _currentDataSet.Rows[rowKey] = newRow;
                    }
                    continue;
                }

                // Update existing row data
                if (rowIndex < _currentDataSet.Rows.Count)
                {
                    var rowKey = _currentDataSet.Rows.Keys.ElementAt(rowIndex);
                    var dataRow = _currentDataSet.Rows[rowKey];

                    dataRow.Data.Clear();
                    var cells = rowContainer.GetChildren();
                    for (int i = 1; i < cells.Count; i++) // Skip checkbox cell at index 0
                    {
                        if (cells[i] is LineEdit cell)
                        {
                            dataRow.Data.Add(cell.Text);
                        }
                    }

                    rowIndex++;
                }
            }
        }
    }

    private void OnAddColumnPressed()
    {
        if (_currentDataSet == null)
            return;

        CommitGridToDataSet();

        _currentDataSet.Columns.Add($"Column {_currentDataSet.Columns.Count + 1}");

        foreach (var row in _currentDataSet.Rows.Values)
        {
            while (row.Data.Count < _currentDataSet.Columns.Count)
                row.Data.Add(string.Empty);
        }

        MapDataSet(_currentDataSet);
    }

    private void OnDeleteColumnPressed()
    {
        if (_currentDataSet == null || _currentDataSet.Columns.Count == 0)
            return;

        CommitGridToDataSet();

        int lastIndex = _currentDataSet.Columns.Count - 1;
        _currentDataSet.Columns.RemoveAt(lastIndex);

        foreach (var row in _currentDataSet.Rows.Values)
        {
            if (row.Data.Count > lastIndex)
                row.Data.RemoveAt(lastIndex);
        }

        MapDataSet(_currentDataSet);
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
        _currentDataSet.Columns = header.ToList();
        _currentDataSet.Rows.Clear();
        _nextRowId = 0;

        for (int i = 1; i < lines.Count; i++)
        {
            var rowKey = _nextRowId.ToString();
            _nextRowId++;
            _currentDataSet.Rows[rowKey] = new DataRow
            {
                Data = NormalizeRowWidth(lines[i], header.Length),
            };
        }

        MapDataSet(_currentDataSet);
    }

    private static List<string> NormalizeRowWidth(string[] cells, int width)
    {
        var list = new List<string>(cells);
        while (list.Count < width)
            list.Add(string.Empty);
        if (list.Count > width)
            list.RemoveRange(width, list.Count - width);
        return list;
    }

    private void MapDataSet(DataSet ds)
    {
        if (_mainContainer == null || ds == null)
            return;

        _currentDataSet = ds;

        ClearSpreadsheet();

        _columnWidths.Clear();
        _rowCheckboxes.Clear();
        for (int i = 0; i < ds.Columns.Count; i++)
        {
            _columnWidths.Add(DefaultColumnWidth);
        }

        // Calculate next row ID
        _nextRowId = 0;
        if (ds.Rows.Count > 0)
        {
            foreach (var key in ds.Rows.Keys)
            {
                if (int.TryParse(key, out int id))
                {
                    _nextRowId = Math.Max(_nextRowId, id + 1);
                }
            }
        }

        CreateHeaderRow(ds);
        CreateDataRows(ds);
        CreateNewRow(ds);
    }

    private void ClearSpreadsheet()
    {
        foreach (var c in _headerContainer.GetChildren())
        {
            c.QueueFree();
        }

        foreach (var c in _dataContainer.GetChildren())
        {
            c.QueueFree();
        }
    }

    private void CreateHeaderRow(DataSet ds)
    {
        // Add checkbox column header
        var checkboxHeader = new PanelContainer();
        checkboxHeader.CustomMinimumSize = new Vector2(CheckboxColumnWidth, HeaderHeight);
        _headerContainer.AddChild(checkboxHeader);

        for (int i = 0; i < ds.Columns.Count; i++)
        {
            var headerCell = new HeaderCell();
            headerCell.SetColumnIndex(i);
            headerCell.SetHeaderText(ds.Columns[i]);
            headerCell.CustomMinimumSize = new Vector2(_columnWidths[i], HeaderHeight);
            headerCell.ColumnResized += OnColumnResized;

            _headerContainer.AddChild(headerCell);
        }
    }

    private void CreateDataRows(DataSet ds)
    {
        foreach (var kv in ds.Rows)
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

            for (int i = 0; i < ds.Columns.Count; i++)
            {
                var cell = new LineEdit();
                cell.Text = i < kv.Value.Data.Count ? kv.Value.Data[i] : string.Empty;
                cell.CustomMinimumSize = new Vector2(_columnWidths[i], RowHeight);
                cell.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                cell.SizeFlagsVertical = Control.SizeFlags.Fill;

                // We are changing this to just save when the user clicks the button rather than as we go.

                rowContainer.AddChild(cell);
            }

            _dataContainer.AddChild(rowContainer);
        }
    }

    private void CreateNewRow(DataSet ds)
    {
        if (ds == null)
            return;

        _newRowContainer = new HBoxContainer();
        _newRowContainer.CustomMinimumSize = new Vector2(0, RowHeight);

        // Add empty checkbox cell
        var checkboxCell = new CenterContainer();
        checkboxCell.CustomMinimumSize = new Vector2(CheckboxColumnWidth, RowHeight);
        _newRowContainer.AddChild(checkboxCell);

        // Add empty LineEdit cells for each column
        for (int i = 0; i < ds.Columns.Count; i++)
        {
            var cell = new LineEdit();
            cell.CustomMinimumSize = new Vector2(_columnWidths[i], RowHeight);
            cell.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            cell.SizeFlagsVertical = Control.SizeFlags.Fill;
            cell.PlaceholderText = "Enter data...";

            // Store column index in metadata for easy retrieval
            cell.SetMeta("column_index", i);
            cell.TextSubmitted += _ => CommitNewRow();

            _newRowContainer.AddChild(cell);
        }

        _dataContainer.AddChild(_newRowContainer);
    }

    private void CommitNewRow()
    {
        if (_currentDataSet == null || _newRowContainer == null)
            return;

        bool hasData = false;
        var rowData = new List<string>();

        for (int i = 1; i < _newRowContainer.GetChildCount(); i++) // Skip checkbox cell at index 0
        {
            if (_newRowContainer.GetChild(i) is LineEdit cell)
            {
                rowData.Add(cell.Text);
                if (!string.IsNullOrWhiteSpace(cell.Text))
                {
                    hasData = true;
                }
            }
        }

        if (!hasData)
            return;

        var rowKey = _nextRowId.ToString();
        _nextRowId++;
        _currentDataSet.Rows[rowKey] = new DataRow { Data = rowData };

        MapDataSet(_currentDataSet);
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
        // Skip the first child (checkbox header)
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

    public void SetProject(Project project)
    {
        _project = project;
        if (_mainContainer != null)
            LoadDatasetList();
    }

    private void OnDeleteButtonPressed()
    {
        if (_currentDataSet == null)
            return;

        var rowsToDelete = new List<string>();

        // Collect row keys for checked checkboxes
        for (int i = 0; i < _rowCheckboxes.Count; i++)
        {
            if (_rowCheckboxes[i].ButtonPressed)
            {
                var rowIndex = i;
                var rowKey = _currentDataSet.Rows.Keys.ElementAt(rowIndex);
                rowsToDelete.Add(rowKey);
            }
        }

        // Remove rows from dataset
        foreach (var key in rowsToDelete)
        {
            _currentDataSet.Rows.Remove(key);
        }

        // Refresh the display if any rows were deleted
        if (rowsToDelete.Count > 0)
        {
            MapDataSet(_currentDataSet);
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

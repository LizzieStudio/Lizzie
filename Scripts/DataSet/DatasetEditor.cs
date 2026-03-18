using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class DatasetEditor : Control
{
    private Project _project;
    private DataSet _currentDataSet;

    private VBoxContainer _mainContainer;
    private HBoxContainer _toolbarContainer;
    private Button _deleteButton;
    private Button _saveButton;
    private Button _cancelButton;

    private HBoxContainer _headerContainer;
    private ScrollContainer _dataScrollContainer;
    private VBoxContainer _dataContainer;
    private Panel _toolSpacer;

    private List<float> _columnWidths = new();
    private List<CheckBox> _rowCheckboxes = new();
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
        InitializeSpreadsheet();

        _project = ProjectService.Instance.CurrentProject;

        if (_project != null)
        {
            MapDataSet(_project.Datasets.First().Value);
        }
    }

    private void InitializeSpreadsheet()
    {
        _mainContainer = GetNode<VBoxContainer>("%MainContainer");
        _mainContainer.SetAnchorsPreset(LayoutPreset.FullRect);

        _deleteButton = GetNode<Button>("%DeleteRow");
        _deleteButton.Pressed += OnDeleteButtonPressed;

        _saveButton = GetNode<Button>("%Save");
        _saveButton.Pressed += SaveDataSet;
        _cancelButton = GetNode<Button>("%Cancel");
        _cancelButton.Pressed += CloseDialog;

        _headerContainer = new HBoxContainer();
        _headerContainer.CustomMinimumSize = new Vector2(0, HeaderHeight);
        _mainContainer.AddChild(_headerContainer);

        _dataScrollContainer = new ScrollContainer();
        _dataScrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _dataScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
        _dataScrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _mainContainer.AddChild(_dataScrollContainer);

        _dataContainer = new VBoxContainer();
        _dataScrollContainer.AddChild(_dataContainer);

        if (_project != null)
            MapDataSet(_project.Datasets.First().Value);
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

        // Refresh the display to show updated data
        //MapDataSet(_currentDataSet);

        GD.Print("Dataset saved successfully");

        EventBus.Instance.Publish<DataSetChangedEvent>(
            new DataSetChangedEvent { DataSetName = _currentDataSet.Name }
        );
        CloseDialog();
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

            for (int i = 0; i < kv.Value.Data.Count; i++)
            {
                var cell = new LineEdit();
                cell.Text = kv.Value.Data[i];
                cell.CustomMinimumSize = new Vector2(_columnWidths[i], RowHeight);
                cell.SizeFlagsHorizontal = SizeFlags.Fill;
                cell.SizeFlagsVertical = SizeFlags.Fill;

                // We are changing this to just save when the user clicks the button rather than as we go.

                // Capture the row key and column index in the lambda
                //var rowKey = kv.Key;
                //var colIndex = i;
                //cell.TextChanged += (newText) => OnCellTextChanged(newText, rowKey, colIndex);

                rowContainer.AddChild(cell);
            }

            _dataContainer.AddChild(rowContainer);
        }
    }

    private void OnCellTextChanged(string newText, string rowKey, int columnIndex)
    {
        if (_currentDataSet == null)
            return;

        // Update the dataset with the new value
        if (_currentDataSet.Rows.TryGetValue(rowKey, out var dataRow))
        {
            if (columnIndex >= 0 && columnIndex < dataRow.Data.Count)
            {
                dataRow.Data[columnIndex] = newText;
            }
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
            cell.SizeFlagsHorizontal = SizeFlags.Fill;
            cell.SizeFlagsVertical = SizeFlags.Fill;
            cell.PlaceholderText = "Enter data...";

            // Store column index in metadata for easy retrieval
            cell.SetMeta("column_index", i);
            cell.TextChanged += OnNewRowTextChanged;

            _newRowContainer.AddChild(cell);
        }

        _dataContainer.AddChild(_newRowContainer);
    }

    private void OnNewRowTextChanged(string newText)
    {
        if (_currentDataSet == null || string.IsNullOrWhiteSpace(newText))
            return;

        // Check if any cell in the new row has data
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

        if (hasData)
        {
            // Add the row to the dataset
            var rowKey = _nextRowId.ToString();
            _nextRowId++;

            var newRow = new DataRow();
            newRow.Data = rowData;
            _currentDataSet.Rows[rowKey] = newRow;

            // Refresh the display to show the new row and create a new blank row
            MapDataSet(_currentDataSet);
        }
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
        {
            MapDataSet(project.Datasets.First().Value);
        }
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

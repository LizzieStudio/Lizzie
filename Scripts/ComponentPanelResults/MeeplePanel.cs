using System;
using System.Collections.Generic;
using Godot;

public partial class MeeplePanel : ComponentPanelDialogResult
{
    private int _gridSize = 8;
    private float _cellSize = 30f;

    private bool[][] _gridState;
    private Panel[,] _gridCells;
    private GridContainer _gridContainer;
    private CenterContainer _matrixContainer;
    private OptionButton _gridSizeOptionButton;

    private bool _isMouseDown = false;
    private bool _isRightMouseDown = false;

    private Color _onColor = new Color(0.2f, 0.6f, 1.0f); // Blue
    private Color _offColor = new Color(0.15f, 0.15f, 0.15f); // Dark gray
    private Color _hoverColor = new Color(0.3f, 0.3f, 0.3f); // Light gray

    private LineEdit _nameInput;
    private LineEdit _heightInput;
    private LineEdit _thicknessInput;
    private ColorPickerButton _colorPicker;
    private ComponentPreview _preview;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Cube;
        _nameInput = GetNode<LineEdit>("%ItemName");
        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += t => UpdatePreview();

        _thicknessInput = GetNode<LineEdit>("%Width");
        _thicknessInput.TextChanged += t => UpdatePreview();

        _colorPicker = GetNode<ColorPickerButton>("%Color");
        _colorPicker.ColorChanged += ColorPickerOnColorChanged;
        _preview = GetNode<ComponentPreview>("%Preview");

        _gridSizeOptionButton = GetNode<OptionButton>("%GridSize");
        _gridSizeOptionButton.ItemSelected += GridSizeOptionButtonChanged;

        InitializeGrid();
        CreateGridUI();
    }

    private void GridSizeOptionButtonChanged(long index)
    {
        switch (index)
        {
            case 0:
                _gridSize = 8;
                _cellSize = 30f;
                break;
            case 1:
                // 8x8
                _gridSize = 12;
                _cellSize = 20f;
                break;
            case 2:
                // 16x16
                _gridSize = 16;
                _cellSize = 15f;
                break;
        }

        InitializeGrid();
        CreateGridUI();
        UpdatePreview();
    }

    private void ColorPickerOnColorChanged(Color color)
    {
        UpdatePreview();
    }

    public override void Activate()
    {
        _preview.SetComponent(GetPreviewComponent(), new Vector3(Mathf.DegToRad(80), 0, 0));
        UpdatePreview();
    }

    private VcMeeple GetPreviewComponent()
    {
        var scene = GD.Load<PackedScene>("res://Scenes/VisualComponents/VcMeeple.tscn");
        return scene.Instantiate<VcMeeple>();
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                _isMouseDown = mouseButton.Pressed;
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                _isRightMouseDown = mouseButton.Pressed;
            }
        }
    }

    private void InitializeGrid()
    {
        _gridState = new bool[_gridSize][];
        _gridCells = new Panel[_gridSize, _gridSize];

        // Initialize all cells to off
        for (int row = 0; row < _gridSize; row++)
        {
            _gridState[row] = new bool[_gridSize];
            for (int col = 0; col < _gridSize; col++)
            {
                _gridState[row][col] = false;
            }
        }
    }

    private void CreateGridUI()
    {
        _matrixContainer = GetNode<CenterContainer>("%MatrixContainer");

        foreach (var c in _matrixContainer.GetChildren())
        {
            c.QueueFree();
        }

        // Create a centered container
        //var centerContainer = new CenterContainer();
        //centerContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        //matrixContainer.AddChild(centerContainer);

        // Create the grid container
        _gridContainer = new GridContainer();
        _gridContainer.Columns = _gridSize;
        _gridContainer.AddThemeConstantOverride("h_separation", 2);
        _gridContainer.AddThemeConstantOverride("v_separation", 2);
        _matrixContainer.AddChild(_gridContainer);

        // Create grid cells
        for (int row = 0; row < _gridSize; row++)
        {
            for (int col = 0; col < _gridSize; col++)
            {
                var cell = CreateGridCell(row, col);
                _gridCells[row, col] = cell;
                _gridContainer.AddChild(cell);
            }
        }
    }

    private Panel CreateGridCell(int row, int col)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(_cellSize, _cellSize);
        panel.MouseFilter = MouseFilterEnum.Pass;

        // Create StyleBox for the panel
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = _offColor;
        styleBox.BorderColor = new Color(0.4f, 0.4f, 0.4f);
        styleBox.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", styleBox);

        // Add mouse click handling
        panel.GuiInput += (inputEvent) => OnCellInput(inputEvent, row, col);

        // Add hover effects
        panel.MouseEntered += () => OnCellMouseEntered(panel, row, col);
        panel.MouseExited += () => OnCellMouseExited(panel, row, col);

        return panel;
    }

    private void OnCellInput(InputEvent @event, int row, int col)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                SetCellOn(row, col);
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                SetCellOff(row, col);
            }
        }
    }

    private void SetCellOn(int row, int col)
    {
        if (!_gridState[row][col])
        {
            _gridState[row][col] = true;
            UpdateCellVisual(row, col);
            UpdatePreview();
        }
    }

    private void SetCellOff(int row, int col)
    {
        if (_gridState[row][col])
        {
            _gridState[row][col] = false;
            UpdateCellVisual(row, col);
            UpdatePreview();
        }
    }

    private void UpdateCellVisual(int row, int col)
    {
        var panel = _gridCells[row, col];
        var styleBox = panel.GetThemeStylebox("panel") as StyleBoxFlat;

        if (styleBox != null)
        {
            styleBox.BgColor = _gridState[row][col] ? _onColor : _offColor;
        }
    }

    private void OnCellMouseEntered(Panel panel, int row, int col)
    {
        // Handle drag painting
        if (_isMouseDown)
        {
            SetCellOn(row, col);
        }
        else if (_isRightMouseDown)
        {
            SetCellOff(row, col);
        }

        // Hover effect for off cells
        var styleBox = panel.GetThemeStylebox("panel") as StyleBoxFlat;
        if (styleBox != null && !_gridState[row][col])
        {
            //styleBox.BorderColor = new Color(0.8f, 0.8f, 0.8f);
            styleBox.SetBorderWidthAll(2);
        }
    }

    private void OnCellMouseExited(Panel panel, int row, int col)
    {
        var styleBox = panel.GetThemeStylebox("panel") as StyleBoxFlat;
        if (styleBox != null && !_gridState[row][col])
        {
            styleBox.BorderColor = new Color(0.4f, 0.4f, 0.4f);
            styleBox.SetBorderWidthAll(1);
        }
    }

    #region Public API

    /// <summary>
    /// Get the current state of the grid
    /// </summary>
    public bool[][] GetGridState()
    {
        // Deep copy of jagged array
        var copy = new bool[_gridSize][];
        for (int i = 0; i < _gridSize; i++)
        {
            copy[i] = new bool[_gridSize];
            Array.Copy(_gridState[i], copy[i], _gridSize);
        }
        return copy;
    }

    /// <summary>
    /// Set the state of a specific cell
    /// </summary>
    public void SetCell(int row, int col, bool state)
    {
        if (row >= 0 && row < _gridSize && col >= 0 && col < _gridSize)
        {
            _gridState[row][col] = state;
            UpdateCellVisual(row, col);
        }
    }

    /// <summary>
    /// Set the entire grid state
    /// </summary>
    public void SetGridState(bool[][] state)
    {
        if (state.Length != _gridSize || (state.Length > 0 && state[0].Length != _gridSize))
        {
            GD.PrintErr($"Invalid grid state size. Expected {_gridSize}x{_gridSize}");
            return;
        }

        for (int row = 0; row < _gridSize; row++)
        {
            for (int col = 0; col < _gridSize; col++)
            {
                _gridState[row][col] = state[row][col];
                UpdateCellVisual(row, col);
            }
        }
    }

    /// <summary>
    /// Clear all cells (set to off)
    /// </summary>
    public void ClearGrid()
    {
        for (int row = 0; row < _gridSize; row++)
        {
            for (int col = 0; col < _gridSize; col++)
            {
                _gridState[row][col] = false;
                UpdateCellVisual(row, col);
            }
        }
    }

    /// <summary>
    /// Fill all cells (set to on)
    /// </summary>
    public void FillGrid()
    {
        for (int row = 0; row < _gridSize; row++)
        {
            for (int col = 0; col < _gridSize; col++)
            {
                _gridState[row][col] = true;
                UpdateCellVisual(row, col);
            }
        }
    }

    /// <summary>
    /// Invert all cells
    /// </summary>
    public void InvertGrid()
    {
        for (int row = 0; row < _gridSize; row++)
        {
            for (int col = 0; col < _gridSize; col++)
            {
                _gridState[row][col] = !_gridState[row][col];
                UpdateCellVisual(row, col);
            }
        }
    }

    #endregion

    public override List<string> Validity()
    {
        return new List<string>();
    }

    public override Dictionary<string, object> GetParams()
    {
        var d = new Dictionary<string, object>();

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", ParamToFloat(_heightInput.Text));
        d.Add("Thickness", ParamToFloat(_thicknessInput.Text));
        d.Add("Color", _colorPicker.Color);
        d.Add("Grid", _gridState);

        return d;
    }

    private void UpdatePreview()
    {
        var d = new Dictionary<string, object>();

        //normalize the size
        var h = ParamToFloat(_heightInput.Text);
        var t = ParamToFloat(_thicknessInput.Text);

        if (h == 0 || t == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        //normalize dimensions to 10x10x10 outer extants
        var scale = 10f / h;

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", 10f);
        d.Add("Thickness", t * scale);
        d.Add("Color", _colorPicker.Color);
        d.Add("Grid", _gridState);

        _preview.Build(d, TextureFactory);
    }

    public override void DisplayPrototype(Guid prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        _nameInput.Text = prototype.Name;
        _heightInput.Text = prototype.Parameters["Height"].ToString();
        _thicknessInput.Text = prototype.Parameters["Thickness"].ToString();
        _colorPicker.Color = (Color)prototype.Parameters["Color"];

        if (
            prototype.Parameters.ContainsKey("Grid")
            && prototype.Parameters["Grid"] is bool[][] grid
        )
        {
            SetGridState(grid);
        }

        Activate();
    }
}

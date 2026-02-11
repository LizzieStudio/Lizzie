using Godot;
using System;
using System.Collections.Generic;

public partial class MeeplePanel : ComponentPanelDialogResult
{
	private const int GridSize = 8;
	private const float CellSize = 15f;
	
	private bool[,] _gridState;
	private Panel[,] _gridCells;
	private GridContainer _gridContainer;
    private MarginContainer _matrixContainer;
	
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

        InitializeGrid();
		CreateGridUI();
	}

    private void ColorPickerOnColorChanged(Color color)
    {
        UpdatePreview();
    }

    public override void Activate()
    {
        _preview.SetComponent(GetPreviewComponent(), new Vector3(Mathf.DegToRad(-10), 0, 0));
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
		_gridState = new bool[GridSize, GridSize];
		_gridCells = new Panel[GridSize, GridSize];
		
		// Initialize all cells to off
		for (int row = 0; row < GridSize; row++)
		{
			for (int col = 0; col < GridSize; col++)
			{
				_gridState[row, col] = false;
			}
		}
	}
	
	private void CreateGridUI()
    {
        _matrixContainer = GetNode<MarginContainer>("%MatrixContainer");

		// Create a centered container
		//var centerContainer = new CenterContainer();
		//centerContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		//matrixContainer.AddChild(centerContainer);
		
		// Create the grid container
		_gridContainer = new GridContainer();
		_gridContainer.Columns = GridSize;
		_gridContainer.AddThemeConstantOverride("h_separation", 2);
		_gridContainer.AddThemeConstantOverride("v_separation", 2);
        _matrixContainer.AddChild(_gridContainer);
		
		// Create grid cells
		for (int row = 0; row < GridSize; row++)
		{
			for (int col = 0; col < GridSize; col++)
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
		panel.CustomMinimumSize = new Vector2(CellSize, CellSize);
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
		if (!_gridState[row, col])
		{
			_gridState[row, col] = true;
			UpdateCellVisual(row, col);
            UpdatePreview();
        }
	}
	
	private void SetCellOff(int row, int col)
	{
		if (_gridState[row, col])
		{
			_gridState[row, col] = false;
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
			styleBox.BgColor = _gridState[row, col] ? _onColor : _offColor;
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
		if (styleBox != null && !_gridState[row, col])
		{
			//styleBox.BorderColor = new Color(0.8f, 0.8f, 0.8f);
			styleBox.SetBorderWidthAll(2);
		}
	}
	
	private void OnCellMouseExited(Panel panel, int row, int col)
	{
		var styleBox = panel.GetThemeStylebox("panel") as StyleBoxFlat;
		if (styleBox != null && !_gridState[row, col])
		{
			styleBox.BorderColor = new Color(0.4f, 0.4f, 0.4f);
			styleBox.SetBorderWidthAll(1);
		}
	}
	
	#region Public API
	
	/// <summary>
	/// Get the current state of the grid
	/// </summary>
	public bool[,] GetGridState()
	{
		return (bool[,])_gridState.Clone();
	}
	
	/// <summary>
	/// Set the state of a specific cell
	/// </summary>
	public void SetCell(int row, int col, bool state)
	{
		if (row >= 0 && row < GridSize && col >= 0 && col < GridSize)
		{
			_gridState[row, col] = state;
			UpdateCellVisual(row, col);
		}
	}
	
	/// <summary>
	/// Set the entire grid state
	/// </summary>
	public void SetGridState(bool[,] state)
	{
		if (state.GetLength(0) != GridSize || state.GetLength(1) != GridSize)
		{
			GD.PrintErr($"Invalid grid state size. Expected {GridSize}x{GridSize}");
			return;
		}
		
		for (int row = 0; row < GridSize; row++)
		{
			for (int col = 0; col < GridSize; col++)
			{
				_gridState[row, col] = state[row, col];
				UpdateCellVisual(row, col);
			}
		}
	}
	
	/// <summary>
	/// Clear all cells (set to off)
	/// </summary>
	public void ClearGrid()
	{
		for (int row = 0; row < GridSize; row++)
		{
			for (int col = 0; col < GridSize; col++)
			{
				_gridState[row, col] = false;
				UpdateCellVisual(row, col);
			}
		}
	}
	
	/// <summary>
	/// Fill all cells (set to on)
	/// </summary>
	public void FillGrid()
	{
		for (int row = 0; row < GridSize; row++)
		{
			for (int col = 0; col < GridSize; col++)
			{
				_gridState[row, col] = true;
				UpdateCellVisual(row, col);
			}
		}
	}
	
	/// <summary>
	/// Invert all cells
	/// </summary>
	public void InvertGrid()
	{
		for (int row = 0; row < GridSize; row++)
		{
			for (int col = 0; col < GridSize; col++)
			{
				_gridState[row, col] = !_gridState[row, col];
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
        var w = ParamToFloat(_thicknessInput.Text);
        
        if (h == 0 || w == 0 )
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        //normalize dimensions to 10x10x10 outer extants
        var scale = 10f / h;

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", 10f);
        d.Add("Width", w * scale);
        d.Add("Color", _colorPicker.Color);
        d.Add("Grid", _gridState);

        _preview.Build(d, TextureFactory);

    }
}

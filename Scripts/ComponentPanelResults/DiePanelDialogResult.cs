using Godot;
using System;
using System.Collections.Generic;

public partial class DiePanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _diameterInput;
	private OptionButton _sidesInput;
	private ColorPickerButton _dieColor;

	private TabContainer _tabContainer;
	private ComponentPreview _preview;

	[Export] private LineEdit[] _quickSideValues;
	[Export] private Label[] _quickSideLabels;

	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Cube;
		_nameInput = GetNode<LineEdit>("%ComponentName");
		_diameterInput = GetNode<LineEdit>("%Diameter");
		_diameterInput.TextChanged += text => UpdatePreview();
		
		_sidesInput = GetNode<OptionButton>("%Sides");
		_sidesInput.ItemSelected += SidesInputOnItemSelected;
		
		_dieColor = GetNode<ColorPickerButton>("%DieColor");
		_dieColor.ColorChanged += color => UpdatePreview();
		
		_preview = GetNode<ComponentPreview>("%Preview");

		_tabContainer = GetNode<TabContainer>("%TabContainer");
		_tabContainer.CurrentTab = 0;
		

		foreach (var l in _quickSideValues)
		{
			l.TextChanged += text => UpdatePreview();
		}
		
		PrototypeIndex = 1;
		UpdateQuickSidesVisibility();
	}
	
	
	public override void Activate()
	{
		var comp = GetPreviewComponent();
		_preview.SetComponent(comp, new Vector3(Mathf.DegToRad(-45),0,0));
		UpdatePreview();
	}

	private VcDie GetPreviewComponent()
	{
		string shape = "VcD6s.tscn";
		
		switch (_sidesInput.Selected)
		{
			case 0:
				shape = "vc_d_4.tscn";
				break;
			
			case 1:
				shape = "VcD6s.tscn";
				break;
			
			case 2:
				shape = "VcD8.tscn";
				break;
			
			case 3:
				shape = "VcD10.tscn";
				break;
			
			case 4:
				shape = "VcD12.tscn";
				break;
			
			
			case 5:
				shape = "VcD20.tscn";
				break;
		}
		
		var scene = GD.Load<PackedScene>($"res://Scenes/VisualComponents/Dice/{shape}");
		var vc = scene.Instantiate<VcDie>();
		
		vc.Ready += UpdatePreview;
		return vc;
	}

	public override void Deactivate()
	{
		_preview.ClearComponent();
	}


	private void SidesInputOnItemSelected(long index)
	{
		UpdateQuickSidesVisibility();
		PrototypeIndex = (int)index;
		Activate();
	}

	private void UpdateQuickSidesVisibility()
	{
		if (_quickSideLabels.Length < 20) return;
		
		if (int.TryParse(_sidesInput.Text, out var target))
		{
			for (int i = 0; i < 20; i++)
			{
				_quickSideLabels[i].Visible = (i < target);
				_quickSideValues[i].Visible = (i < target);
			}
		}
			
	}
	

	public override List<string> Validity()
	{
		return new List<string>();
	}

	public override Dictionary<string, object> GetParams()
	{
		var d = new Dictionary<string, object>();

		d.Add("ComponentName", _nameInput.Text);
		d.Add("Size", ParamToFloat(_diameterInput.Text));
		d.Add("Color", _dieColor.Color);
		d.Add("Sides", PackageSides());
		return d;
	}

	private string[] PackageSides()
	{
		if (!int.TryParse(_sidesInput.Text, out var sides)) return Array.Empty<string>();

		var s = new string[sides];

		for (int i = 0; i < sides; i++)
		{
			s[i] = _quickSideValues[i].Text;
		}

		return s;

	}

	private void UpdatePreview()
	{
		//normalize the size
		var dia = ParamToFloat(_diameterInput.Text);
		if (dia == 0)
		{
			_preview.SetComponentVisibility(false);
			return;
		}

		dia = 16;

		_preview.SetComponentVisibility(true);

		var d = new Dictionary<string, object>();

		d.Add("ComponentName", _nameInput.Text);
		d.Add("Size", dia / 2);
		d.Add("Color", _dieColor.Color);
		d.Add("Sides", PackageSides());
		
		_preview.Build(d, TextureFactory);
	}
}

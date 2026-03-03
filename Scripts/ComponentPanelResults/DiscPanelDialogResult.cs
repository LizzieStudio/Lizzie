using Godot;
using System;
using System.Collections.Generic;

public partial class DiscPanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _heightInput;
	private LineEdit _diameterInput;
	
	private ColorPickerButton _colorPicker;
	private ComponentPreview _preview;
	
	
	
	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Disc;
		_nameInput = GetNode<LineEdit>("%ItemName");
		_heightInput = GetNode<LineEdit>("%Height");
		_heightInput.TextChanged += t => UpdatePreview();
		
		_diameterInput = GetNode<LineEdit>("%Diameter");
		_diameterInput.TextChanged += t => UpdatePreview();
		
		_colorPicker = GetNode<ColorPickerButton>("%Color");
		_colorPicker.ColorChanged += color => UpdatePreview();
		
		_preview = GetNode<ComponentPreview>("%Preview");
		
	}
	
	public override void _Process(double delta)
	{
		//_previewDisc.Rotation += new Vector3(0,(float)delta, 0);
	}
	
	public override void Activate()
	{
		_preview.SetComponent(GetPreviewComponent(), new Vector3(Mathf.DegToRad(-10),0,0));
		UpdatePreview();
	}

	private VcDisc GetPreviewComponent()
	{
		var scene = GD.Load<PackedScene>("res://Scenes/VisualComponents/VcDisc.tscn");
		return scene.Instantiate<VcDisc>();
	}

	public override void Deactivate()
	{
		_preview.ClearComponent();
	}

	public override List<string> Validity()
	{
		var ret = new List<string>();
		
		if (string.IsNullOrEmpty(_nameInput.Text.Trim()))
		{
			ret.Add("Component Name required");
		}

		return ret;
	}

	public override Dictionary<string, object> GetParams()
	{
		var d = new Dictionary<string, object>();

		d.Add("ComponentName", _nameInput.Text);
		d.Add("Height", ParamToFloat(_heightInput.Text));
		d.Add("Diameter", ParamToFloat((_diameterInput).Text));
		d.Add("Color", _colorPicker.Color);

		return d;
	}
	
	private void UpdatePreview()
	{
		var d = new Dictionary<string, object>();

		var h = ParamToFloat(_heightInput.Text);
		var dia = ParamToFloat(_diameterInput.Text);

		if (h == 0 || dia == 0)
		{
			_preview.SetComponentVisibility(false);
			return;
		}

		_preview.SetComponentVisibility(true);

		//normalize dimensions to 10x10x10 outer extants
		var scale = 10f / Math.Max(h, dia);

		d.Add("ComponentName", _nameInput.Text);
		d.Add("Height", h * scale);
		d.Add("Diameter", dia * scale);
		d.Add("Color", _colorPicker.Color);

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
		_diameterInput.Text = prototype.Parameters["Diameter"].ToString();
		_colorPicker.Color = (Color)prototype.Parameters["Color"];

		UpdatePreview();
	}
}

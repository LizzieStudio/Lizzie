using Godot;
using System;
using System.Collections.Generic;

public partial class CubePanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _heightInput;
	private LineEdit _widthInput;
	private LineEdit _lengthInput;
	private ColorPickerButton _colorPicker;
	
	
	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Cube;
		_nameInput = GetNode<LineEdit>("GridContainer/ItemName");
		_heightInput = GetNode<LineEdit>("GridContainer/HBoxContainer3/Height");
		_lengthInput = GetNode<LineEdit>("GridContainer/HBoxContainer4/Length");
		_widthInput = GetNode<LineEdit>("GridContainer/HBoxContainer2/Width");
		_colorPicker = GetNode<ColorPickerButton>("GridContainer/Color");
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
		GD.Print("Added Name");
		d.Add("Height", ParamToFloat(_heightInput.Text));
		GD.Print("Added Height");
		d.Add("Width", ParamToFloat(_widthInput.Text));
		GD.Print("Added Width");
		d.Add("Length", ParamToFloat(_lengthInput.Text));
		GD.Print("Added Length");
		d.Add("Color", _colorPicker.Color);
		GD.Print("Added Color");

		foreach (var p in d)
		{
			GD.Print($"{p.Key} {p.Value}");
		}
		return d;
	}


}

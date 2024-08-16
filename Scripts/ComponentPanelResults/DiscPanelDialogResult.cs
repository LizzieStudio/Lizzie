using Godot;
using System;
using System.Collections.Generic;

public partial class DiscPanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _heightInput;
	private LineEdit _diameterInput;
	
	private ColorPickerButton _colorPicker;
	
	
	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Disc;
		_nameInput = GetNode<LineEdit>("GridContainer/ItemName");
		_heightInput = GetNode<LineEdit>("GridContainer/HBoxContainer3/Height");
		_diameterInput = GetNode<LineEdit>("GridContainer/HBoxContainer4/Diameter");
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
		d.Add("Height", ParamToFloat(_heightInput.Text));
		d.Add("Diameter", ParamToFloat((_diameterInput).Text));
		d.Add("Color", _colorPicker.Color);

		return d;
	}
}

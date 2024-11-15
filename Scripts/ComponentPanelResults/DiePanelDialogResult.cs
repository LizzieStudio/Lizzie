using Godot;
using System;
using System.Collections.Generic;

public partial class DiePanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _diameterInput;
	private OptionButton _sidesInput;
	private ColorPickerButton _dieColor;
	
	
	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Cube;
		_nameInput = GetNode<LineEdit>("%ComponentName");
		_diameterInput = GetNode<LineEdit>("%Diameter");
		_sidesInput = GetNode<OptionButton>("%Sides");
		_sidesInput.ItemSelected += SidesInputOnItemSelected;
		_dieColor = GetNode<ColorPickerButton>("%DieColor");
		PrototypeIndex = 1;
	}

	private void SidesInputOnItemSelected(long index)
	{
		PrototypeIndex = (int)index;
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

		return d;
	}
}

using Godot;
using System;
using System.Collections.Generic;

public partial class BoardPanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _heightInput;
	private LineEdit _widthInput;
	
	private LineEdit _frontImage;
	private LineEdit _backImage;

	private Button _frontButton;
	private Button _backButton;
	
	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Card;
		
		
		_nameInput = GetNode<LineEdit>("GridContainer/ItemName");
		_heightInput = GetNode<LineEdit>("GridContainer/HBoxContainer3/Height");
		_widthInput = GetNode<LineEdit>("GridContainer/HBoxContainer4/Width");

		_frontImage = GetNode<LineEdit>("GridContainer/HBoxContainer5/FrontFile");
		_backImage = GetNode<LineEdit>("GridContainer/HBoxContainer6/BackFile");
		
		_frontButton = GetNode<Button>("GridContainer/HBoxContainer5/Button");
		_frontButton.Pressed += GetFrontFile;
		_backButton = GetNode<Button>("GridContainer/HBoxContainer6/Button");
		_backButton.Pressed += GetBackFile;
	}

	private void GetFrontFile()
	{
		ShowFileDialog("Select Front Image File", FrontFileSelected);
	}
	
	private void FrontFileSelected(string file)
	{
		if (!string.IsNullOrEmpty(file))
		{
			_frontImage.Text = file;
		}
	}

	private void GetBackFile()
	{
		ShowFileDialog("Select Back Image File", BackFileSelected);
	}
	private void BackFileSelected(string file)
	{
		if (!string.IsNullOrEmpty(file))
		{
			_backImage.Text = file;
		}
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
		d.Add("Width", ParamToFloat(_widthInput.Text));
		d.Add("FrontImage", _frontImage.Text);
		d.Add("BackImage", _backImage.Text);
		d.Add("Type", VcToken.TokenType.Board);

		return d;
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
		_widthInput.Text = prototype.Parameters["Width"].ToString();
		_frontImage.Text = prototype.Parameters["FrontImage"].ToString();
		_backImage.Text = prototype.Parameters["BackImage"].ToString();
	}
}

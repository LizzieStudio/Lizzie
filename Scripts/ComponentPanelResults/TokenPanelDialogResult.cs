using Godot;
using System;
using System.Collections.Generic;

public partial class TokenPanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
		private LineEdit _heightInput;
		private LineEdit _widthInput;
		private LineEdit _thicknessInput;
		
		private LineEdit _frontImage;
		private LineEdit _backImage;
	
		private Button _frontButton;
		private Button _backButton;

		private ColorPickerButton _quickBackgroundColor;
		private ColorPickerButton _quickTextColor;
		private LineEdit _quickText;

		private ColorRect _previewRect;
		private Label _previewText;

		//quick method back of token
		private ColorPickerButton _quickBackgroundColor2;
		private ColorPickerButton _quickTextColor2;
		private LineEdit _quickText2;

		private ColorRect _previewRect2;
		private Label _previewText2;
		
		private CheckBox _quickBackCheckbox;
		
		public override void _Ready()
		{
			ComponentType = VisualComponentBase.VisualComponentType.Card;
			
			
			_nameInput = GetNode<LineEdit>("HBoxContainer/VBoxContainer/GridContainer/ItemName");
			_heightInput = GetNode<LineEdit>("HBoxContainer/VBoxContainer/GridContainer/HBoxContainer3/Height");
			_widthInput = GetNode<LineEdit>("HBoxContainer/VBoxContainer/GridContainer/HBoxContainer4/Width");
			_thicknessInput = GetNode<LineEdit>("HBoxContainer/VBoxContainer/GridContainer/HBoxContainer7/Thickness");
	
			_frontImage = GetNode<LineEdit>("HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Custom/GridContainer/HBoxContainer5/FrontFile");
			_backImage = GetNode<LineEdit>("HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Custom/GridContainer/HBoxContainer6/BackFile");
			
			_frontButton = GetNode<Button>("HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Custom/GridContainer/HBoxContainer5/Button");
			_frontButton.Pressed += GetFrontFile;
			_backButton = GetNode<Button>("HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Custom/GridContainer/HBoxContainer6/Button");
			_backButton.Pressed += GetBackFile;

			_quickBackgroundColor = GetNode<ColorPickerButton>(
				"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer/ColorPickerButton");
			_quickText =
				GetNode<LineEdit>(
					"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer2/LineEdit");
			_quickTextColor = GetNode<ColorPickerButton>(
				"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer2/ColorPickerButton");
			_quickBackCheckbox =
				GetNode<CheckBox>(
					"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer3/CheckBox");
			
			_quickText.TextChanged += OnTextChange;
			_quickBackgroundColor.ColorChanged += OnBackgroundColorChanged;
			_quickTextColor.ColorChanged += OnPreviewTextColorChange;
			_quickBackCheckbox.Pressed += OnQuickBackCheckboxChange;
			
			_quickBackgroundColor2 = GetNode<ColorPickerButton>(
				"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer5/ColorPickerButton");
			_quickText2 =
				GetNode<LineEdit>(
					"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer4/LineEdit");
			_quickTextColor2 = GetNode<ColorPickerButton>(
				"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer4/ColorPickerButton");
			
			_quickText2.TextChanged += OnText2Change;
			_quickBackgroundColor2.ColorChanged += OnBackgroundColor2Changed;
			_quickTextColor2.ColorChanged += OnPreviewTextColor2Change;
			
			
			
			_previewRect = GetNode<ColorRect>("HBoxContainer/PanelContainer/VBoxContainer/Panel/SubViewportContainer/SubViewport/ColorRect");
			_previewText = GetNode<Label>("HBoxContainer/PanelContainer/VBoxContainer/Panel/SubViewportContainer/SubViewport/Label");
			
			_previewRect2 = GetNode<ColorRect>("HBoxContainer/PanelContainer/VBoxContainer/Panel2/SubViewportContainer/SubViewport/ColorRect");
			_previewText2 = GetNode<Label>("HBoxContainer/PanelContainer/VBoxContainer/Panel2/SubViewportContainer/SubViewport/Label");

			OnQuickBackCheckboxChange();	//just to set the initial line visibility in case someone messed with the control.
		}

		private void OnQuickBackCheckboxChange()
		{
			var h4 = GetNode<HBoxContainer>(
				"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer4");
			
			var h5 = GetNode<HBoxContainer>(
				"HBoxContainer/VBoxContainer/MarginContainer/TabContainer/Quick/MarginContainer/VBoxContainer/HBoxContainer5");

			h4.Visible = _quickBackCheckbox.ButtonPressed;
			h5.Visible = _quickBackCheckbox.ButtonPressed;

			var preview = GetNode<Panel>("HBoxContainer/PanelContainer/VBoxContainer/Panel2");
			preview.Visible = _quickBackCheckbox.ButtonPressed;
		}

		private void OnPreviewTextColorChange(Color color)
		{
			_previewText.LabelSettings.FontColor = color;
		}

		private void OnBackgroundColorChanged(Color color)
		{
			_previewRect.Color = color;
		}

		private void OnTextChange(string newtext)
		{
			_previewText.Text = newtext;
		}

		private void OnPreviewTextColor2Change(Color color)
		{
			_previewText2.LabelSettings.FontColor = color;
		}

		private void OnBackgroundColor2Changed(Color color)
		{
			_previewRect2.Color = color;
		}

		private void OnText2Change(string newtext)
		{
			_previewText2.Text = newtext;
		}
		
		private void GetFrontFile()
		{
			ShowFileDialog("Select Front Image File", FrontFileSelected);
		}

		private Texture GetQuickTexture()
		{
			return GetNode<SubViewport>(
				"HBoxContainer/PanelContainer/VBoxContainer/Panel/SubViewportContainer/SubViewport").GetTexture();
		}
		
		private Texture GetQuickTexture2()
		{
			var t =  GetNode<SubViewport>(
				"HBoxContainer/PanelContainer/VBoxContainer/Panel2/SubViewportContainer/SubViewport").GetTexture();
			var i = t.GetImage();
			i.FlipX();

			var it = new ImageTexture();
			it.SetImage(i);
			return it;
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
			d.Add("Thickness", ParamToFloat(_thicknessInput.Text));
			d.Add("FrontImage", _frontImage.Text);
			d.Add("BackImage", _backImage.Text);
			d.Add("QuickTexture", GetQuickTexture());		//TODO we can just pass in the colors and the text string
			d.Add("QuickTextureBack", GetQuickTexture2());  //and have the texture created by the BUILD routine
			
			return d;
		}
}

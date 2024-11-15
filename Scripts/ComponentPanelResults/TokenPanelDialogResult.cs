using Godot;
using System.Collections.Generic;
using System.IO;

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

	private HBoxContainer _customBackRow;

	private ColorPickerButton _quickBackgroundColor;
	private ColorPickerButton _quickTextColor;
	private LineEdit _quickText;


	//quick method back of token
	private ColorPickerButton _quickBackgroundColor2;
	private ColorPickerButton _quickTextColor2;
	private LineEdit _quickText2;


	private CheckBox _quickBackCheckbox;
	private CheckBox _customBackCheckbox;

	private OptionButton _shapePicker;

	private TabContainer _tabs;

	private TextureRect _clipRect;
	private TextureRect _textureRect;
	private Label _label;


	//private SubViewportContainer _topViewportContainer;
	private PreviewPanel _topPreview;
	private PreviewPanel _bottomPreview;

	public override void _Ready()
	{
		ComponentType = VisualComponentBase.VisualComponentType.Token;


		_nameInput = GetNode<LineEdit>("%ItemName");
		_heightInput = GetNode<LineEdit>("%Height");
		_heightInput.TextChanged += HeightWidthTextChanged;

		_widthInput = GetNode<LineEdit>("%Width");
		_widthInput.TextChanged += HeightWidthTextChanged;

		_thicknessInput = GetNode<LineEdit>("%Thickness");

		//Custom
		_frontImage = GetNode<LineEdit>("%FrontFile");
		_backImage = GetNode<LineEdit>("%BackFile");
		_customBackCheckbox = GetNode<CheckBox>("%CustomDifferentBack");
		_customBackCheckbox.Pressed += OnCustomBackCheckboxChange;

		_customBackRow = GetNode<HBoxContainer>("%CustomBackFileRow");

		_frontButton = GetNode<Button>("%FrontFileButton");
		_frontButton.Pressed += GetFrontFile;
		_backButton = GetNode<Button>("%BackFileButton");
		_backButton.Pressed += GetBackFile;

		_quickBackgroundColor = GetNode<ColorPickerButton>("%TopBgColor");
		_quickText =
			GetNode<LineEdit>("%TopCaption");
		_quickTextColor = GetNode<ColorPickerButton>("%TopTextColor");
		_quickBackCheckbox =
			GetNode<CheckBox>("%ToggleBack");


		_quickText.TextChanged += OnTextChange;
		_quickBackgroundColor.ColorChanged += OnBackgroundColorChanged;
		_quickTextColor.ColorChanged += OnPreviewTextColorChange;
		_quickBackCheckbox.Pressed += OnQuickBackCheckboxChange;

		_quickBackgroundColor2 = GetNode<ColorPickerButton>("%BottomBgColor");
		_quickText2 =
			GetNode<LineEdit>("%BottomCaption");
		_quickTextColor2 = GetNode<ColorPickerButton>("%BottomTextColor");

		_quickText2.TextChanged += OnText2Change;
		_quickBackgroundColor2.ColorChanged += OnBackgroundColor2Changed;
		_quickTextColor2.ColorChanged += OnPreviewTextColor2Change;

		_shapePicker = GetNode<OptionButton>("%ShapePicker");
		_shapePicker.ItemSelected += ShapePickerOnItemSelected;


		_tabs = GetNode<TabContainer>("%Tabs");
		_tabs.TabSelected += OnTabSelected;

		_topPreview = GetNode<PreviewPanel>("%TopPreview");
		_bottomPreview = GetNode<PreviewPanel>("%BottomPreview");

		OnQuickBackCheckboxChange(); //just to set the initial line visibility in case someone messed with the control.
		OnCustomBackCheckboxChange();

		OnTabSelected(0);

		ShapePickerOnItemSelected(0);
	}
	private void HeightWidthTextChanged(string newtext)
	{
		if (float.TryParse(_heightInput.Text, out var h) && float.TryParse(_widthInput.Text, out var w))
		{
			_topPreview.SetSize(w, h);
			_bottomPreview.SetSize(w, h);
		}
	}

	private void OnCustomBackCheckboxChange()
	{
		_customBackRow.Visible = _customBackCheckbox.ButtonPressed;

		_bottomPreview.Visible = _customBackCheckbox.ButtonPressed;
	}


	private void OnTabSelected(long tab)
	{
		switch (tab)
		{
			case 0:
				_bottomPreview.SetViewPortMode(PreviewPanel.ShapeViewportMode.Shape);
				_topPreview.SetViewPortMode(PreviewPanel.ShapeViewportMode.Shape);
				_bottomPreview.Visible = _quickBackCheckbox.ButtonPressed;
				break;

			case 1:
				_bottomPreview.SetViewPortMode(PreviewPanel.ShapeViewportMode.Texture);
				_topPreview.SetViewPortMode(PreviewPanel.ShapeViewportMode.Texture);
				_bottomPreview.Visible = _customBackCheckbox.ButtonPressed;
				break;
		}
	}

	private void ShapePickerOnItemSelected(long index)
	{
		TokenTextureSubViewport.TokenShape shape = TokenTextureSubViewport.TokenShape.Square;

		switch (index)
		{
			case 0:
				shape = TokenTextureSubViewport.TokenShape.Square;
				break;

			case 1:
				shape = TokenTextureSubViewport.TokenShape.Circle;
				break;
			case 2:
				shape = TokenTextureSubViewport.TokenShape.HexPoint;
				break;
			case 3:
				shape = TokenTextureSubViewport.TokenShape.HexFlat;
				break;
		}

		PrototypeIndex = (int)index;
		_topPreview.SetShape(shape);

		_bottomPreview.SetShape(shape);
	}

	private void OnQuickBackCheckboxChange()
	{
		var h4 = GetNode<HBoxContainer>("%BottomBgContainer");

		var h5 = GetNode<HBoxContainer>("%BottomCaptionContainer");

		h4.Visible = _quickBackCheckbox.ButtonPressed;
		h5.Visible = _quickBackCheckbox.ButtonPressed;

		_bottomPreview.Visible = _quickBackCheckbox.ButtonPressed;
	}

	private void OnPreviewTextColorChange(Color color)
	{
		_topPreview.SetTextColor(color);
	}

	private void OnBackgroundColorChanged(Color color)
	{
		_topPreview.SetBackgroundColor(color);
	}

	private void OnTextChange(string newtext)
	{
		_topPreview.SetText(newtext);
	}

	private void OnPreviewTextColor2Change(Color color)
	{
		_bottomPreview.SetTextColor(color);
	}

	private void OnBackgroundColor2Changed(Color color)
	{
		_bottomPreview.SetBackgroundColor(color);
	}

	private void OnText2Change(string newtext)
	{
		_bottomPreview.SetText(newtext);
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
			if (File.Exists(_frontImage.Text))
			{
				var t = LoadTexture(_frontImage.Text);
				_topPreview.SetViewPortMode(PreviewPanel.ShapeViewportMode.Texture);
				_topPreview.SetTexture(t);
			}
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
			if (File.Exists(file))
			{
				var t = LoadTexture(file);
				_bottomPreview.SetViewPortMode(PreviewPanel.ShapeViewportMode.Texture);
				_bottomPreview.SetTexture(t);
			}
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

	private ImageTexture LoadTexture(string filename)
	{
		var image = new Image();
		var err = image.Load(filename);

		if (err == Error.Ok)
		{
			var texture = new ImageTexture();
			texture.SetImage(image);
			return texture;
		}

		return new ImageTexture();
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
		d.Add("Shape", _shapePicker.Selected);
		d.Add("Mode", _tabs.CurrentTab);
		d.Add("FrontBgColor", _quickBackgroundColor.Color);
		d.Add("FrontCaption", _quickText.Text);
		d.Add("FrontCaptionColor", _quickTextColor.Color);
		d.Add("Type", VcToken.TokenType.Token);
		d.Add("FrontFontSize", 24);
		
		if (_tabs.CurrentTab == 0)
		{
			d.Add("DifferentBack", _quickBackCheckbox.ButtonPressed);
		}
		else
		{
			d.Add("DifferentBack", _customBackCheckbox.ButtonPressed);
		}


		d.Add("BackBgColor", _quickBackgroundColor2.Color);
		d.Add("BackCaption", _quickText2.Text);
		d.Add("BackCaptionColor", _quickTextColor2.Color);
		d.Add("BackFontSize", 24);

		return d;
	}
}

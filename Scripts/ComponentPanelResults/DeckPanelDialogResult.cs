using Godot;
using System;
using System.Collections.Generic;

public partial class DeckPanelDialogResult : MarginContainer
{
	private LineEdit _nameInput;
	private LineEdit _heightInput;
	private LineEdit _widthInput;


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
	private CardPreviewPanel _preview;


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_heightInput = GetNode<LineEdit>("%Height");
		_heightInput.TextChanged += HeightWidthChange;

		_widthInput = GetNode<LineEdit>("%Width");
		_widthInput.TextChanged += HeightWidthChange;

		_preview = GetNode<CardPreviewPanel>("%Preview");

		HeightWidthChange(string.Empty); //just to start
	}

	private void HeightWidthChange(string newtext)
	{
		if (float.TryParse(_heightInput.Text, out var h) && float.TryParse(_widthInput.Text, out var w))
		{
			_preview.SetSize(w, h);
		}
	}

	private Color[] _suits;
	private int _suitCount;

	private String[] _cardString;

	private List<QuickCardData> GenerateCards()
	{
		var d = new List<QuickCardData>();

		for (int i = 0; i < _suitCount; i++)
		{
			var values = ComponentPanelDialogResult.ParseValueRanges(_cardString[i]);

			foreach (var v in values)
			{
				var c = new QuickCardData
				{
					BackgroundColor = _suits[i],
					Caption = v.ToString()
				};

				d.Add(c);
			}
		}

		return d;
	}
}

public class QuickCardData
{
	public Color BackgroundColor { get; set; }
	public string Caption { get; set; }
}

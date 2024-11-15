using Godot;
using System;
using System.Collections.Generic;
using System.Xml;

public partial class DeckPanelDialogResult : ComponentPanelDialogResult
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

	private Button _nextCardPreview;
	private Button _prevCardPreview;
	private Button _firstCardPreview;
	private Button _lastCardPreview;
	private Label _curCardPreview;

	private const int MaxQuickSuitCount = 8;
	private ColorPickerButton[] _quickSuitColors = new ColorPickerButton[MaxQuickSuitCount];
	private LineEdit[] _quickSuitValues = new LineEdit[MaxQuickSuitCount];
	private HBoxContainer[] _quickSuitRows = new HBoxContainer[MaxQuickSuitCount];
	private OptionButton _quickSuitCount;
	
	private ColorPickerButton _quickBackColor;
	private LineEdit _quickBackText;


	private OptionButton _cardSizes;
	
	private CardPreviewPanel _preview;
	private OptionButton _previewFrontBack;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		InitializeBinding();
		InitializeStandardSizes();
		
		HeightWidthChange(string.Empty); //just to start

		QuickSuitCountChanged(_quickSuitCount.Selected);
		GenerateCards();
		ChangePreviewCard(0);
	}

	private Dictionary<string, (float, float)> _standardSizes;

	private void InitializeStandardSizes()
	{
		_standardSizes = new();
		_standardSizes.Add("Poker", (2.5f, 3.5f));
		_standardSizes.Add("Bridge", (2.25f, 3.5f));
		_standardSizes.Add("Mini Euro", (1.75f, 2.5f));
		_standardSizes.Add("Tarot", (2.75f, 4.75f));
		_standardSizes.Add("Custom", (0,0));

		_cardSizes.Clear();
		foreach (var kv in _standardSizes)
		{
			_cardSizes.AddItem(kv.Key);
		}
	}
	
	
	private void StandardSizeChanged(long index)
	{
		var cardType = _cardSizes.Text;

		if (!_standardSizes.TryGetValue(cardType, out var size)) return;

		var w = size.Item1;
		var h = size.Item2;

		if (w == 0 || h == 0) return;
		
		float conversion = 25.4f;

		_heightInput.Text = (h * conversion).ToString("f1");
		_widthInput.Text = (w * conversion).ToString("f1");

		HeightWidthChange(string.Empty);
	}

	private bool _isBound;
	private void InitializeBinding()
	{
		if (_isBound) return;

		_isBound = true;
		
		_nameInput = GetNode<LineEdit>("%ItemName");
		
		_heightInput = GetNode<LineEdit>("%Height");
		_heightInput.TextChanged += HeightWidthChange;

		_widthInput = GetNode<LineEdit>("%Width");
		_widthInput.TextChanged += HeightWidthChange;

		_preview = GetNode<CardPreviewPanel>("%Preview");

		_prevCardPreview = GetNode<Button>("%PrevCard");
		_prevCardPreview.ButtonDown += () => ChangePreviewCard(-1);

		_nextCardPreview = GetNode<Button>("%NextCard");
		_nextCardPreview.ButtonDown += () => ChangePreviewCard(1);
		
		_firstCardPreview = GetNode<Button>("%FirstCard");
		_firstCardPreview.ButtonDown += () => ShowPreviewCard(0);

		_lastCardPreview = GetNode<Button>("%LastCard");
		_lastCardPreview.ButtonDown += () => ShowPreviewCard(_quickCards.Count-1);

		_curCardPreview = GetNode<Label>("%CurCardLabel");
		_previewFrontBack = GetNode<OptionButton>("%PreviewFrontBack");
		_previewFrontBack.ItemSelected += l => ShowPreviewCard(_curCard);

		_tabs = GetNode<TabContainer>("%TabContainer");
		_cardSizes = GetNode<OptionButton>("%StandardSize");
		_cardSizes.ItemSelected += StandardSizeChanged;
		
		//Quick suit selections - There's a better way to do this, (instantiating the lines), but for now...
		for (int i = 0; i < MaxQuickSuitCount; i++)
		{
			_quickSuitColors[i] = GetNode<ColorPickerButton>($"%QuickSuit{i + 1}Color");
			_quickSuitValues[i] = GetNode<LineEdit>($"%QuickSuit{i + 1}Contents");
			_quickSuitRows[i] = GetNode<HBoxContainer>($"%QSRow{i + 1}");

			_quickSuitColors[i].ColorChanged += color => GenerateCards();
			_quickSuitValues[i].TextChanged += t => GenerateCards();
		}

		_quickSuitCount = GetNode<OptionButton>("%QuickSuitCount");
		_quickSuitCount.ItemSelected += QuickSuitCountChanged;
		
		_quickBackColor = GetNode<ColorPickerButton>("%QuickBackColor");
		_quickBackColor.ColorChanged += c => GenerateCards();
		
		_quickBackText = GetNode<LineEdit>("%QuickBackText");
		_quickBackText.TextChanged += t => GenerateCards();

	}




	private int _curCard = 0;
	private List<QuickCardData> _quickCards = new();
	private List<QuickCardData> _quickSuits = new();



	private void ChangePreviewCard(int direction)
	{
		if (_quickCards.Count == 0) return;
		
		_curCard += direction;
		_curCard = Mathf.Clamp(_curCard, 0, _quickCards.Count - 1);
		
		_preview.DisplayQuickCard(_quickCards[_curCard], _previewFrontBack.Text == "Front");

		_curCardPreview.Text = $"Card {_curCard + 1} of {_quickCards.Count}";
	}
	
	private void ShowPreviewCard(int cardId)
	{
		if (_quickCards.Count == 0) return;
		
		_curCard = cardId;
		_curCard = Mathf.Clamp(_curCard, 0, _quickCards.Count - 1);
		
		_preview.DisplayQuickCard(_quickCards[_curCard], _previewFrontBack.Text == "Front");

		_curCardPreview.Text = $"Card {_curCard + 1} of {_quickCards.Count}";
	}
	
	
	private void HeightWidthChange(string newtext)
	{
		if (float.TryParse(_heightInput.Text, out var h) && float.TryParse(_widthInput.Text, out var w))
		{
			_preview.SetSize(w, h);
		}
	}

	
	private int _suitCount;

	

	private void GenerateCards()
	{
		_suitCount = _quickSuitCount.Selected + 1;

		_quickCards.Clear();

		for (int i = 0; i < _suitCount; i++)
		{
			var values = Utility.ParseValueRanges(_quickSuitValues[i].Text);

			foreach (var v in values)
			{
				var c = new QuickCardData
				{
					BackgroundColor = _quickSuitColors[i].Color,
					Caption = v,
					CardBackColor = _quickBackColor.Color,
					CardBackValue = _quickBackText.Text
				};

				_quickCards.Add(c);
			}
		}

		ChangePreviewCard(0);
	}

	public override List<string> Validity()
	{
		return new List<string>();
	}
	
	public override Dictionary<string, object> GetParams()
	{
		
		var d = new Dictionary<string, object>();
		
		d.Add("ComponentName", _nameInput.Text);
		d.Add("Height", ParamToFloat(_heightInput.Text));
		d.Add("Width", ParamToFloat(_widthInput.Text));
		d.Add("Mode", _tabs.CurrentTab);
		
		if (_tabs.CurrentTab == 0)
		{
			LoadQuickSuits();
			d.Add("QuickCardData", _quickSuits);
		}
		
		return d;
	}

	private void QuickSuitCountChanged(long suitCount)
	{
		for (int i = 0; i < _quickSuitRows.Length; i++)
		{
			_quickSuitRows[i].Visible = (suitCount > i - 1);
		}
		
		GenerateCards();
	}
	
	private void LoadQuickSuits()
	{
		_quickSuits.Clear();

		_suitCount = _quickSuitCount.Selected + 1;
		
		for (int i = 0; i < _suitCount; i++)
		{
			var suit = new QuickCardData
			{
				BackgroundColor = _quickSuitColors[i].Color,
				Caption = _quickSuitValues[i].Text,
				CardBackColor = _quickBackColor.Color,
				CardBackValue =  _quickBackText.Text
			};
			
			_quickSuits.Add(suit);
			
		}

	}
}

public class QuickCardData
{
	public Color BackgroundColor { get; set; }
	public string Caption { get; set; }
	
	public Color CardBackColor { get; set; }
	public string CardBackValue { get; set; }
}



using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

public partial class DeckPanelDialogResult : ComponentPanelDialogResult
{
	private LineEdit _nameInput;
	private LineEdit _heightInput;
	private LineEdit _widthInput;


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

	//private OptionButton _shapePicker;

	private TabContainer _tabs;

	private TextureRect _clipRect;
	private TextureRect _textureRect;
	private Label _label;

	private const int MaxQuickSuitCount = 8;
	private ColorPickerButton[] _quickSuitColors = new ColorPickerButton[MaxQuickSuitCount];
	private LineEdit[] _quickSuitValues = new LineEdit[MaxQuickSuitCount];
	private HBoxContainer[] _quickSuitRows = new HBoxContainer[MaxQuickSuitCount];
	private OptionButton _quickSuitCount;
	
	private ColorPickerButton _quickBackColor;
	private LineEdit _quickBackText;


	private OptionButton _cardSizes;

	//Grid Tab elements
	private LineEdit _gridFrontImageFile;
	private Button _gridFrontImageButton;

	private LineEdit _gridBackImageFile;
	private Button _gridBackImageButton;

	private LineEdit _gridRowCount;
	private LineEdit _gridColCount;
	private LineEdit _gridCardCount;

	private CheckButton _gridSingleBack;
	
	private ComponentPreview _componentPreview;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		InitializeBinding();
		InitializeStandardSizes();
		
		HeightWidthChange(string.Empty); //just to start

		QuickSuitCountChanged(_quickSuitCount.Selected);
		GenerateQuickCards();
		ChangePreviewCard(0);
	}

	public override void Activate()
	{
		var comp = GetPreviewComponent();
		_componentPreview.SetComponent(comp, new Vector3(Mathf.DegToRad(90),0,0));
		UpdatePreview();
	}

	private VcToken GetPreviewComponent()
	{
		string shape = "VcToken.tscn";

		var scene = GD.Load<PackedScene>($"res://Scenes/VisualComponents/{shape}");
		var vc = scene.Instantiate<VcToken>();

		vc.Ready += UpdatePreview;
		return vc;
	}

	public override void Deactivate()
	{
		_componentPreview.ClearComponent();
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
		
		_componentPreview = GetNode<ComponentPreview>("%ComponentPreview");
		_componentPreview.MultiItemMode = true;
		_componentPreview.ItemSelected += ComponentPreviewOnItemSelected;
		
		_tabs = GetNode<TabContainer>("%TabContainer");
		_tabs.TabChanged += t => UpdatePreview();
		
		_cardSizes = GetNode<OptionButton>("%StandardSize");
		_cardSizes.ItemSelected += StandardSizeChanged;
		
		InitializeQuickBindings();

		InitializeGridBindings();
	}

	private void InitializeQuickBindings()
	{
		//Quick suit selections - There's a better way to do this, (instantiating the lines), but for now...
		for (int i = 0; i < MaxQuickSuitCount; i++)
		{
			_quickSuitColors[i] = GetNode<ColorPickerButton>($"%QuickSuit{i + 1}Color");
			_quickSuitValues[i] = GetNode<LineEdit>($"%QuickSuit{i + 1}Contents");
			_quickSuitRows[i] = GetNode<HBoxContainer>($"%QSRow{i + 1}");

			_quickSuitColors[i].ColorChanged += color => GenerateQuickCards();
			_quickSuitValues[i].TextChanged += t => GenerateQuickCards();
		}

		_quickSuitCount = GetNode<OptionButton>("%QuickSuitCount");
		_quickSuitCount.ItemSelected += QuickSuitCountChanged;
		
		_quickBackColor = GetNode<ColorPickerButton>("%QuickBackColor");
		_quickBackColor.ColorChanged += c => GenerateQuickCards();
		
		_quickBackText = GetNode<LineEdit>("%QuickBackText");
		_quickBackText.TextChanged += t => GenerateQuickCards();

	}

	private void InitializeGridBindings()
	{
		_gridFrontImageFile = GetNode<LineEdit>("%GridFront");
		_gridFrontImageFile.TextChanged += LoadFrontGridFile;
		_gridFrontImageButton = GetNode<Button>("%GridFrontButton");
		_gridFrontImageButton.Pressed += GetFrontFile;

		_gridBackImageFile = GetNode<LineEdit>("%GridBack");
		_gridBackImageFile.TextChanged += LoadBackGridFile;
		
		_gridBackImageButton = GetNode<Button>("%GridBackButton");
		_gridBackImageButton.Pressed += GetBackFile;

		_gridRowCount = GetNode<LineEdit>("%GridRows");
		_gridRowCount.TextChanged += t => GenerateGridCards();
		_gridColCount = GetNode<LineEdit>("%GridCols");
		_gridColCount.TextChanged += t => GenerateGridCards();
		_gridCardCount = GetNode<LineEdit>("%GridCardCount");
		_gridCardCount.TextChanged += t => GenerateGridCards();
		
		_gridSingleBack = GetNode<CheckButton>("%GridSingleBack");
		_gridSingleBack.Pressed += () => GenerateQuickCards();
	}

	private void LoadFrontGridFile(string newtext)
	{
		//Get the texture
		if (File.Exists(_gridFrontImageFile.Text))
		{
			_frontMasterSprite = Utility.LoadTexture(_gridFrontImageFile.Text);
		}
		else
		{
			_frontMasterSprite = null;
		}
		
		UpdatePreview();
	}

	
	private void LoadBackGridFile(string newtext)
	{
		//Get the texture
		if (File.Exists(_gridBackImageFile.Text))
		{
			_backMasterSprite = Utility.LoadTexture(_gridBackImageFile.Text);
		}
		else
		{
			_backMasterSprite = null;
		}
		
		UpdatePreview();
	}
	
	private void GetFrontFile()
	{
		ShowFileDialog("Select Front Image File", FrontFileSelected);
	}

	private void FrontFileSelected(string file)
	{
		_gridFrontImageFile.Text = file;

		_frontMasterSprite = Utility.LoadTexture(file);
		
		UpdatePreview();
		return;
	}

	private void GetBackFile()
	{
		ShowFileDialog("Select Back Image File", BackFileSelected);
	}

	private void BackFileSelected(string file)
	{
		_gridBackImageFile.Text = file;
		UpdatePreview();
		return;
	}
	private void ComponentPreviewOnItemSelected(object sender, ItemSelectedEventArgs e)
	{
		_curCard = e.Index;
		UpdatePreview();
	}


	private int _curCard = 0;
	private List<QuickCardData> _quickCards = new();
	private List<QuickCardData> _quickSuits = new();



	private void ChangePreviewCard(int direction)
	{
		if (_componentPreview.ItemCount == 0) return;
		
		_curCard += direction;
		_curCard = Mathf.Clamp(_curCard, 0, _componentPreview.ItemCount - 1);
		
		UpdatePreview();
	}
	
	private void ShowPreviewCard(int cardId)
	{
		if (_componentPreview.ItemCount == 0) return;
		
		_curCard = cardId;
		_curCard = Mathf.Clamp(_curCard, 0, _componentPreview.ItemCount - 1);
		
		UpdatePreview();
	}
	
	
	private void HeightWidthChange(string newtext)
	{
		UpdatePreview();
	}


	private int _gridRows;
	private int _gridCols;
	private int _gridCount;
	private void GenerateGridCards()
	{
		int.TryParse(_gridRowCount.Text, out _gridRows);
		int.TryParse(_gridColCount.Text, out _gridCols);
		int.TryParse(_gridCardCount.Text, out _gridCount);
		
		_componentPreview.ItemCount = _gridCount;
		
		ChangePreviewCard(0);
	}


	private int _suitCount;
	private void GenerateQuickCards()
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
		_componentPreview.ItemCount = _quickCards.Count;
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
		d.Add("Shape",0);

		switch (_tabs.CurrentTab)
		{
			case 0:
				AddQuickParameters(d);
				break;
			
			case 1:	//Grid
				AddGridParameters(d);
				break;
		}
		

		return d;
	}

	private void AddQuickParameters(Dictionary<string, object> d)
	{
		LoadQuickSuits();
		d.Add("QuickCardData", _quickSuits);
		d.Add("Mode", VcToken.TokenBuildMode.Quick);
	}
	private void AddGridParameters(Dictionary<string, object> d)
	{
		d.Add("FrontMasterSprite", _frontMasterSprite);
		d.Add("GridRows", _gridRows);
		d.Add("GridCols", _gridCols);
		d.Add("GridCount", _gridCols);
		
		d.Add("Mode", VcToken.TokenBuildMode.Grid);
		d.Add("DifferentBack", false);
	}
	
	

	private void QuickSuitCountChanged(long suitCount)
	{
		for (int i = 0; i < _quickSuitRows.Length; i++)
		{
			_quickSuitRows[i].Visible = (suitCount > i - 1);
		}
		
		GenerateQuickCards();
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
	
	private void UpdatePreview()
	{
		//normalize the size
		var h = ParamToFloat(_heightInput.Text);
		var w = ParamToFloat(_widthInput.Text);
		if (h == 0 || w == 0)
		{
			_componentPreview.SetComponentVisibility(false);
			return;
		}

		_componentPreview.SetComponentVisibility(true);
		
		//normalize dimensions to 10x10x10 outer extants
		var scale = 10f / Math.Max(h, w);
		
		
		var d = new Dictionary<string, object>();

		d.Add("ComponentName", _nameInput.Text);
		d.Add("Height", h * scale);
		d.Add("Width", w * scale);
		d.Add("Thickness", 0.3f);
		d.Add("Type", VcToken.TokenType.Card);
		
		
		var card = _componentPreview.CurrentItem;

		switch (_tabs.CurrentTab)
		{
			case 0:
				UpdateQuick(d, card);
				break;
			case 1:
				UpdateGrid(d, card);
				break;
		}
		
		_componentPreview.Build(d, TextureFactory);
		
	}

	private Texture2D _frontMasterSprite;
	private Texture2D _backMasterSprite;
	private void UpdateGrid(Dictionary<string, object> d, int card)
	{
		
		d.Add("FrontMasterSprite", _frontMasterSprite);
		d.Add("GridRows", _gridRows);
		d.Add("GridCols", _gridCols);
		d.Add("GridIndex", card);
		
		d.Add("Shape",0);
		d.Add("Mode", VcToken.TokenBuildMode.Grid);
		d.Add("DifferentBack", false);
		
	}

	private void UpdateQuick(Dictionary<string, object> d, int card)
	{
		
		if (card < 0 || card >= _quickCards.Count)
		{
			GD.PrintErr("Invalid card # in deck UpdatePreview");
			return;
		}

		var qc = _quickCards[card];

		foreach (var param in QuickCardParams(qc))
		{
			d.Add(param.Key, param.Value);
		}

	}

	private Dictionary<string, object> QuickCardParams(QuickCardData cardData)
	{
		var p = new Dictionary<string, object>();


		p.Add("Shape",0);
		p.Add("Mode", VcToken.TokenBuildMode.Quick);
		p.Add("FrontBgColor", cardData.BackgroundColor); 
		p.Add("FrontCaption", cardData.Caption);
		p.Add("FrontCaptionColor", Colors.Black);
		p.Add("DifferentBack", true);
		p.Add("BackBgColor", cardData.CardBackColor);
		p.Add("BackCaption", cardData.CardBackValue);
		p.Add("BackCaptionColor", Colors.Black);
		p.Add("FrontFontSize", 72);
		p.Add("BackFontSize", 24);

		return p;
	}
}



public class QuickCardData
{
	public Color BackgroundColor { get; set; }
	public string Caption { get; set; }
	
	public Color CardBackColor { get; set; }
	public string CardBackValue { get; set; }
}



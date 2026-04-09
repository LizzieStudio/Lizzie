using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Godot;

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

    private OptionButton _frontTemplatePicker;
    private Button _editFrontTemplateButton;
    private OptionButton _backTemplatePicker;
    private Button _editBackTemplateButton;
    private OptionButton _datasetPicker;
    private Button _datasetEditorButton;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        InitializeBinding();
        InitializeStandardSizes();
        InitializeTemplates();

        ComponentType = VisualComponentBase.VisualComponentType.Deck;

        HeightWidthChange(string.Empty); //just to start

        QuickSuitCountChanged(_quickSuitCount.Selected);
        GenerateQuickCards();
        ChangePreviewCard(0);

        //register for events
        EventBus.Instance.Subscribe<TemplateChangedEvent>(TemplateChanged);
        EventBus.Instance.Subscribe<DataSetChangedEvent>(DataSetChanged);
    }

    private void TemplateChanged(TemplateChangedEvent obj)
    {
        UpdatePreview();
    }

    private void DataSetChanged(DataSetChangedEvent obj)
    {
        UpdatePreview();
    }

    public override void Activate()
    {
        var comp = GetPreviewComponent();
        _componentPreview.SetComponent(comp, new Vector3(Mathf.DegToRad(90), 0, 0));
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
        _standardSizes.Add("Custom", (0, 0));

        _cardSizes.Clear();
        foreach (var kv in _standardSizes)
        {
            _cardSizes.AddItem(kv.Key);
        }
    }

    private void InitializeTemplates()
    {
        _frontTemplatePicker = GetNode<OptionButton>("%FrontTemplateList");
        _frontTemplatePicker.ItemSelected += OnFrontTemplateChanged;
        _editFrontTemplateButton = GetNode<Button>("%EditFrontTemplateButton");
        _editFrontTemplateButton.Pressed += EditFrontTemplate;

        _backTemplatePicker = GetNode<OptionButton>("%BackTemplateList");
        _backTemplatePicker.ItemSelected += OnBackTemplateChanged;
        _editBackTemplateButton = GetNode<Button>("%EditBackTemplateButton");
        _editBackTemplateButton.Pressed += EditBackTemplate;

        _datasetPicker = GetNode<OptionButton>("%DatasetList");
        _datasetPicker.ItemSelected += OnDatasetChanged;

        _datasetEditorButton = GetNode<Button>("%EditDatasetButton");
        _datasetEditorButton.Pressed += EditDataset;

        UpdateTemplateTab();
    }

    private void EditFrontTemplate()
    {
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateName = _frontTemplate?.Name });
    }

    private void EditBackTemplate()
    {
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateName = _backTemplate?.Name });
    }

    private void EditDataset()
    {
        EventBus.Instance.Publish(
            new ShowDatasetEditor { DatasetName = _textureContext.DataSet?.Name }
        );
    }

    private void StandardSizeChanged(long index)
    {
        var cardType = _cardSizes.Text;

        if (!_standardSizes.TryGetValue(cardType, out var size))
            return;

        var w = size.Item1;
        var h = size.Item2;

        if (w == 0 || h == 0)
            return;

        float conversion = 25.4f;

        _heightInput.Text = (h * conversion).ToString("f1");
        _widthInput.Text = (w * conversion).ToString("f1");

        HeightWidthChange(string.Empty);
    }

    private bool _isBound;

    private void InitializeBinding()
    {
        if (_isBound)
            return;

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
        if (_componentPreview.ItemCount == 0)
            return;

        _curCard += direction;
        _curCard = Mathf.Clamp(_curCard, 0, _componentPreview.ItemCount - 1);

        UpdatePreview();
    }

    private void ShowPreviewCard(int cardId)
    {
        if (_componentPreview.ItemCount == 0)
            return;

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
        LoadQuickSuits();

        _quickCards = TemplateEngine.GenerateQuickCards(_quickSuits, _suitCount);

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
                    CardBackValue = _quickBackText.Text,
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
        d.Add("Shape", 0);

        switch (_tabs.CurrentTab)
        {
            case 0:
                AddQuickParameters(d);
                break;

            case 1: //Grid
                AddGridParameters(d);
                break;

            case 2:
                AddTemplateParameters(d);
                break;
        }

        return d;
    }

    private void AddQuickParameters(Dictionary<string, object> d)
    {
        LoadQuickSuits();
        d.Add("QuickCardData", _quickSuits);
        d.Add("DifferentBack", true);
        d.Add("Mode", VcToken.TokenBuildMode.QuickDeck);
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

    private void AddTemplateParameters(Dictionary<string, object> d)
    {
        d.Add("Mode", VcToken.TokenBuildMode.Template);

        /*
        if (_textureContext.DataSet == null)
        {
            _textureContext.CurrentRowName = string.Empty;
        }
        else
        {
            var cardName = _textureContext.DataSet.Rows.ElementAt(card).Key;
            _textureContext.CurrentRowName = cardName;
        }
        */

        if (_frontTemplate != null)
        {
            d.Add("FrontTemplate", _frontTemplate.Name);
        }

        if (_backTemplate != null)
        {
            d.Add("BackTemplate", _backTemplate.Name);
        }
        else if (_frontTemplate != null)
        {
            d.Add("BackTemplate", _frontTemplate.Name);
        }

        d.Add("Dataset", _textureContext.DataSet?.Name);
        //d.Add("CardReference", _textureContext.CurrentRowName);
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
                CardBackValue = _quickBackText.Text,
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

        const int dpi = 250;

        _textureContext.ParentSize = new Vector2(350, 250);
        _textureContext.Dpi = 100; //TODO - Figure out what this should be

        var card = _componentPreview.CurrentItem;

        /*
        var d = new Dictionary<string, object>();

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", h * scale);
        d.Add("Width", w * scale);
        d.Add("Thickness", 0.3f);
        d.Add("Type", VcToken.TokenType.Card);

        switch (_tabs.CurrentTab)
        {
            case 0:
                UpdateQuick(d, card);
                break;
            case 1:
                UpdateGrid(d, card);
                break;

            case 2:
                UpdateTemplate(d, card);
                break;
        }
        */

        var d = GetParams();

        Utility.UpdateParam(d, "Height", h * scale);
        Utility.UpdateParam(d, "Width", w * scale);
        _componentPreview.Build(d, GetRow(card), TextureFactory);
    }

    private string GetRow(int card)
    {
        switch (_tabs.CurrentTab)
        {
            case 0: //Quick
            case 1: //Grid
                return (card + 1).ToString();

            case 2:
                if (_textureContext.DataSet == null)
                    return string.Empty;
                if (card < 0 || card >= _textureContext.DataSet.Rows.Count)
                    return string.Empty;
                return _textureContext.DataSet.Rows.ElementAt(card).Key;
        }

        return string.Empty;
    }

    private Texture2D _frontMasterSprite;
    private Texture2D _backMasterSprite;

    private void UpdateGrid(Dictionary<string, object> d, int card)
    {
        d.Add("FrontMasterSprite", _frontMasterSprite);
        d.Add("GridRows", _gridRows);
        d.Add("GridCols", _gridCols);
        d.Add("GridIndex", card);

        d.Add("Shape", 0);
        d.Add("Mode", VcToken.TokenBuildMode.Grid);
        d.Add("DifferentBack", false);
    }

    private TextureContext _textureContext = new();

    private void OnDatasetChanged(long index)
    {
        if (_datasetPicker.Selected == 0)
        {
            _textureContext.DataSet = null;
            _textureContext.CurrentRowName = null;
            _componentPreview.MultiItemMode = false;
        }
        else
        {
            _textureContext.DataSet = _currentProject.Datasets[
                _datasetPicker.GetItemText((int)index)
            ];
            _componentPreview.MultiItemMode = true;
            _componentPreview.SetItemLabels(_textureContext.DataSet.Rows.Keys.ToList());
        }

        UpdatePreview();
    }

    private Template _frontTemplate;
    private Template _backTemplate;

    private void OnFrontTemplateChanged(long index)
    {
        if (_frontTemplatePicker.Selected == 0)
        {
            _frontTemplate = null;
        }
        else
        {
            _frontTemplate = _currentProject.Templates[
                _frontTemplatePicker.GetItemText((int)index)
            ];
        }

        UpdatePreview();
    }

    private void OnBackTemplateChanged(long index)
    {
        if (_backTemplatePicker.Selected == 0)
        {
            _backTemplate = null;
        }
        else
        {
            _backTemplate = _currentProject.Templates[_backTemplatePicker.GetItemText((int)index)];
        }

        UpdatePreview();
    }

    private void UpdateTemplate(Dictionary<string, object> d, int card)
    {
        _textureContext.ParentSize = new Vector2(250, 350);
        _textureContext.Dpi = 100;

        d.Add("Mode", VcToken.TokenBuildMode.Template);
        if (_textureContext.DataSet == null)
        {
            _textureContext.CurrentRowName = string.Empty;
        }
        else
        {
            var cardName = _textureContext.DataSet.Rows.ElementAt(card).Key;
            _textureContext.CurrentRowName = cardName;
        }

        if (_frontTemplate != null)
        {
            d.Add("FrontTemplate", _frontTemplate.Name);
        }

        if (_backTemplate != null)
        {
            d.Add("BackTemplate", _backTemplate.Name);
        }
        else if (_frontTemplate != null)
        {
            d.Add("BackTemplate", _frontTemplate.Name);
        }

        d.Add("Dataset", _textureContext.DataSet?.Name);
        d.Add("CardReference", _textureContext.CurrentRowName);
        var c = _componentPreview.GetComponent();
        if (c != null)
            c.DataSetRow = _textureContext.CurrentRowName;
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

        p.Add("Shape", 0);
        p.Add("Mode", VcToken.TokenBuildMode.QuickDeck);
        p.Add("FrontBgColor", cardData.BackgroundColor);
        p.Add("FrontCaption", cardData.Caption);
        p.Add("FrontCaptionColor", Colors.Black);
        p.Add("DifferentBack", true);
        p.Add("BackBgColor", cardData.CardBackColor);
        p.Add("BackCaption", cardData.CardBackValue);
        p.Add("BackCaptionColor", Colors.Black);
        p.Add("FrontFontSize", 72);
        p.Add("BackFontSize", 24);

        var fqt = new QuickTextureField
        {
            ForegroundColor = Colors.Black,
            FaceType = TextureFactory.TextureObjectType.Text,
            Caption = cardData.Caption,
            Quantity = 1,
        };
        p.Add("QuickFront", fqt);

        var bqt = new QuickTextureField
        {
            ForegroundColor = Colors.Black,
            FaceType = TextureFactory.TextureObjectType.Text,
            Caption = cardData.CardBackValue,
            Quantity = 1,
        };
        p.Add("QuickBack", bqt);

        return p;
    }

    private Project _currentProject;
    public override Project CurrentProject
    {
        get => _currentProject;
        set
        {
            _currentProject = value;
            UpdateTemplateTab();
        }
    }

    private void UpdateTemplateTab()
    {
        if (CurrentProject == null || _frontTemplatePicker == null)
            return;

        _frontTemplatePicker.Clear();
        _backTemplatePicker.Clear();

        _frontTemplatePicker.AddItem("(none)");
        _backTemplatePicker.AddItem("(none)");
        foreach (var t in CurrentProject.Templates)
        {
            _frontTemplatePicker.AddItem(t.Key);
            _backTemplatePicker.AddItem(t.Key);
        }

        _datasetPicker.Clear();
        _datasetPicker.AddItem("(none)");
        foreach (var d in CurrentProject.Datasets)
        {
            _datasetPicker.AddItem(d.Key);
        }
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

        if (
            prototype.Parameters.ContainsKey("Mode")
            && prototype.Parameters["Mode"] is VcToken.TokenBuildMode mode
        )
        {
            _tabs.CurrentTab = mode switch
            {
                VcToken.TokenBuildMode.QuickDeck => 0,
                VcToken.TokenBuildMode.Grid => 1,
                VcToken.TokenBuildMode.Template => 2,
                _ => 0,
            };
        }

        // Handle Quick mode parameters
        if (_tabs.CurrentTab == 0 && prototype.Parameters.ContainsKey("QuickCardData"))
        {
            if (prototype.Parameters["QuickCardData"] is List<QuickCardData> quickData)
            {
                _quickSuitCount.Select(quickData.Count - 1);
                for (int i = 0; i < quickData.Count && i < MaxQuickSuitCount; i++)
                {
                    _quickSuitColors[i].Color = quickData[i].BackgroundColor;
                    _quickSuitValues[i].Text = quickData[i].Caption;
                }
                if (quickData.Count > 0)
                {
                    _quickBackColor.Color = quickData[0].CardBackColor;
                    _quickBackText.Text = quickData[0].CardBackValue;
                }
            }
        }

        // Handle Grid mode parameters
        if (_tabs.CurrentTab == 1)
        {
            if (prototype.Parameters.ContainsKey("GridRows"))
            {
                _gridRowCount.Text = prototype.Parameters["GridRows"].ToString();
            }
            if (prototype.Parameters.ContainsKey("GridCols"))
            {
                _gridColCount.Text = prototype.Parameters["GridCols"].ToString();
            }
            if (prototype.Parameters.ContainsKey("GridCount"))
            {
                _gridCardCount.Text = prototype.Parameters["GridCount"].ToString();
            }
        }

        // Handle Template mode parameters
        if (_tabs.CurrentTab == 2)
        {
            if (prototype.Parameters.ContainsKey("FrontTemplate"))
            {
                string frontTemplateName = prototype.Parameters["FrontTemplate"].ToString();
                for (int i = 0; i < _frontTemplatePicker.ItemCount; i++)
                {
                    if (_frontTemplatePicker.GetItemText(i) == frontTemplateName)
                    {
                        _frontTemplatePicker.Select(i);
                        break;
                    }
                }
            }

            if (prototype.Parameters.ContainsKey("BackTemplate"))
            {
                string backTemplateName = prototype.Parameters["BackTemplate"].ToString();
                for (int i = 0; i < _backTemplatePicker.ItemCount; i++)
                {
                    if (_backTemplatePicker.GetItemText(i) == backTemplateName)
                    {
                        _backTemplatePicker.Select(i);
                        break;
                    }
                }
            }

            if (prototype.Parameters.ContainsKey("Dataset"))
            {
                string datasetName = prototype.Parameters["Dataset"].ToString();
                for (int i = 0; i < _datasetPicker.ItemCount; i++)
                {
                    if (_datasetPicker.GetItemText(i) == datasetName)
                    {
                        _datasetPicker.Select(i);
                        break;
                    }
                }
            }
        }

        UpdatePreview();
    }
}

public class QuickCardData
{
    public Color BackgroundColor { get; set; }
    public string Caption { get; set; }

    public Color CardBackColor { get; set; }
    public string CardBackValue { get; set; }
}

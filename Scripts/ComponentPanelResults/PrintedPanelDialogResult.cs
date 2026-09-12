using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.AssetManagement;

public partial class PrintedPanelDialogResult : ComponentPanelDialogResult
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
    private ColorPickerButton _quickBackgroundColor2;

    private CheckBox _quickBackCheckbox;
    private CheckBox _customBackCheckbox;

    private OptionButton _typePicker;
    private OptionButton _shapePicker;
    private Label _orientationLabel;
    private HBoxContainer _orientationRow;
    private Button _portraitButton;
    private Button _landscapeButton;
    private Texture2D _iconPortrait;
    private Texture2D _iconLandscape;
    private Texture2D _iconHexPoint;
    private Texture2D _iconHexFlat;
    private Label _widthLabel;
    private HBoxContainer _widthRow;
    private Label _heightLabel;

    private TabContainer _tabs;

    private ComponentPreview _preview;
    private QuickTextureEntry _frontField;
    private QuickTextureEntry _backField;

    private const int MaxQuickSuitCount = 8;
    private ColorPickerButton[] _quickSuitColors = new ColorPickerButton[MaxQuickSuitCount];
    private LineEdit[] _quickSuitValues = new LineEdit[MaxQuickSuitCount];
    private HBoxContainer[] _quickSuitRows = new HBoxContainer[MaxQuickSuitCount];
    private OptionButton _quickSuitCount;
    private ColorPickerButton _quickBackColor;
    private LineEdit _quickBackText;
    private List<QuickCardData> _quickCards = new();
    private List<QuickCardData> _quickSuits = new();
    private int _suitCount;

    private IconLibrary _iconLibrary = new();

    private OptionButton _frontTemplatePicker;
    private Button _editFrontTemplateButton;
    private OptionButton _backTemplatePicker;
    private Button _editBackTemplateButton;
    private OptionButton _datasetPicker;
    private Button _datasetEditorButton;

    private ImageSelector _gridFrontImageSelector;
    private ImageSelector _gridBackImageSelector;
    private LineEdit _gridRowCount;
    private LineEdit _gridColCount;
    private LineEdit _gridCardCount;
    private CheckButton _gridSingleBack;

    private record ShapePreset(string Label, int Shape, float W = 0f, float H = 0f, float T = 0f);

    private static readonly Dictionary<string, ShapePreset[]> ShapesByType = new()
    {
        ["Card"] = new[]
        {
            new ShapePreset("Poker Card", 0, 63.5f, 88.9f, 0.2f),
            new ShapePreset("Bridge Card", 0, 57.15f, 88.9f, 0.2f),
            new ShapePreset("Mini Euro Card", 0, 44.45f, 63.5f, 0.2f),
            new ShapePreset("Tarot Card", 0, 69.85f, 120.65f, 0.2f),
        },
        ["Token"] = new[]
        {
            new ShapePreset("Rectangle", 0, 25.4f, 25.4f, 1f),
            new ShapePreset("Circle", 1, 25.4f, 25.4f, 1f),
            new ShapePreset("Hex", 2, 25.4f, 25.4f, 1f),
        },
        ["Board"] = new[]
        {
            new ShapePreset("Quad Fold", 0, 254f, 254f, 2f),
            new ShapePreset("Hex Fold", 0, 762f, 254f, 2f),
        },
    };

    private bool _suppressShapeReset;

    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Token;

        _nameInput = GetNode<LineEdit>("%ItemName");

        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += _ =>
        {
            if (!_suppressShapeReset)
                UpdateOrientationButtons();
            UpdatePreview();
        };

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += _ =>
        {
            if (!_suppressShapeReset)
                UpdateOrientationButtons();
            UpdatePreview();
        };

        _thicknessInput = GetNode<LineEdit>("%Thickness");
        _thicknessInput.TextChanged += _ => UpdatePreview();

        _typePicker = GetNode<OptionButton>("%TypePicker");
        _typePicker.ItemSelected += OnTypeChanged;

        _shapePicker = GetNode<OptionButton>("%ShapePicker");
        _shapePicker.ItemSelected += OnShapeChanged;

        _orientationLabel = GetNode<Label>("%OrientationLabel");
        _orientationRow = GetNode<HBoxContainer>("%OrientationRow");
        _portraitButton = GetNode<Button>("%PortraitButton");
        _landscapeButton = GetNode<Button>("%LandscapeButton");
        _portraitButton.Pressed += OnOrientationChanged;
        _landscapeButton.Pressed += OnOrientationChanged;

        _iconPortrait = GD.Load<Texture2D>(
            "res://Textures/UI/crop_portrait_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg"
        );
        _iconLandscape = GD.Load<Texture2D>(
            "res://Textures/UI/crop_landscape_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg"
        );
        _iconHexPoint = GD.Load<Texture2D>("res://Textures/UI/hex_point_up.svg");
        _iconHexFlat = GD.Load<Texture2D>("res://Textures/UI/hex_edge_up.svg");

        _widthLabel = GetNode<Label>("%WidthLabel");
        _widthRow = GetNode<HBoxContainer>("%WidthRow");
        _heightLabel = GetNode<Label>("%HeightLabel");

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
        _quickBackCheckbox = GetNode<CheckBox>("%ToggleBack");
        _frontField = GetNode<QuickTextureEntry>("%FrontField");
        _frontField.FieldChanged += (sender, args) => UpdatePreview();
        _frontField.SetIcons(_iconLibrary);
        _quickBackgroundColor.ColorChanged += _ => UpdatePreview();
        _quickBackCheckbox.Pressed += OnQuickBackCheckboxChange;
        _quickBackgroundColor2 = GetNode<ColorPickerButton>("%BottomBgColor");
        _quickBackgroundColor2.ColorChanged += _ => UpdatePreview();
        _backField = GetNode<QuickTextureEntry>("%BackField");
        _backField.FieldChanged += (sender, args) => UpdatePreview();
        _backField.SetIcons(_iconLibrary);

        _tabs = GetNode<TabContainer>("%Tabs");
        _tabs.TabSelected += tab =>
        {
            if (tab == 1)
                GenerateQuickCards();
            UpdatePreview();
        };

        _preview = GetNode<ComponentPreview>("%Preview");
        _preview.ItemSelected += PreviewOnItemSelected;

        InitializeTemplates();
        InitializeGridBindings();
        InitializeMultiBindings();

        OnQuickBackCheckboxChange();
        OnCustomBackCheckboxChange();
        OnTypeChanged(0);
        QuickSuitCountChanged(3);
        GenerateQuickCards();
    }

    private string TypeKey() =>
        _typePicker.Selected switch
        {
            0 => "Card",
            1 => "Token",
            2 => "Board",
            _ => "Card",
        };

    private ShapePreset GetCurrentShape()
    {
        var cuts = ShapesByType[TypeKey()];
        var idx = Math.Clamp(_shapePicker.Selected, 0, cuts.Length - 1);
        return cuts[idx];
    }

    private int GetEffectiveShape()
    {
        var cut = GetCurrentShape();
        if (cut.Shape == 2 && _landscapeButton.ButtonPressed)
            return 3; // Hex Edge Up
        return cut.Shape;
    }

    private void OnTypeChanged(long _)
    {
        _shapePicker.Clear();
        foreach (var cut in ShapesByType[TypeKey()])
            _shapePicker.AddItem(cut.Label);
        OnShapeChanged(0);
    }

    private void OnShapeChanged(long _)
    {
        var cut = GetCurrentShape();
        if (cut.W != 0f)
        {
            _suppressShapeReset = true;
            _widthInput.Text = cut.W.ToString("f1");
            _heightInput.Text = cut.H.ToString("f1");
            _thicknessInput.Text = cut.T.ToString("f1");
            _suppressShapeReset = false;
        }

        UpdateDimensionUI();

        if (Visible && !_suppressShapeReset)
            Activate();
    }

    private void OnOrientationChanged()
    {
        if (_suppressShapeReset)
            return;
        var cut = GetCurrentShape();
        if (cut.Shape == 0)
        {
            if (
                float.TryParse(_widthInput.Text, out var w)
                && float.TryParse(_heightInput.Text, out var h)
            )
            {
                bool wantLandscape = _landscapeButton.ButtonPressed;
                bool needsSwap = wantLandscape ? h > w : w > h;
                if (needsSwap)
                {
                    _suppressShapeReset = true;
                    (_widthInput.Text, _heightInput.Text) = (_heightInput.Text, _widthInput.Text);
                    _suppressShapeReset = false;
                }
            }
        }

        UpdatePreview();
    }

    private void UpdateOrientationButtons()
    {
        var cut = GetCurrentShape();
        if (cut.Shape != 0)
            return;
        if (
            !float.TryParse(_widthInput.Text, out var w)
            || !float.TryParse(_heightInput.Text, out var h)
        )
            return;
        _landscapeButton.ButtonPressed = w > h;
        _portraitButton.ButtonPressed = h >= w;
    }

    private void UpdateDimensionUI()
    {
        int shape = GetEffectiveShape();
        bool isRect = shape == 0;
        bool isCircle = shape == 1;
        bool isHex = shape == 2 || shape == 3;

        _widthLabel.Visible = isRect;
        _widthRow.Visible = isRect;
        _heightLabel.Text =
            isCircle ? "Diameter"
            : isRect ? "Height"
            : "Size";
        _orientationLabel.Visible = !isCircle;
        _orientationRow.Visible = !isCircle;

        _portraitButton.Icon = isHex ? _iconHexPoint : _iconPortrait;
        _landscapeButton.Icon = isHex ? _iconHexFlat : _iconLandscape;

        UpdateOrientationButtons();
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

    private void InitializeGridBindings()
    {
        _gridFrontImageSelector = GetNode<ImageSelector>("%FrontImageSelector");
        _gridFrontImageSelector.ImageSelected += FrontImageSelected;
        _gridFrontImageSelector.SetProject(ProjectService.Instance.CurrentProject);

        _gridBackImageSelector = GetNode<ImageSelector>("%BackImageSelector");
        _gridBackImageSelector.ImageSelected += BackImageSelected;
        _gridBackImageSelector.SetProject(ProjectService.Instance.CurrentProject);

        _gridRowCount = GetNode<LineEdit>("%GridRows");
        _gridRowCount.TextChanged += _ => GenerateGridTokens();
        _gridColCount = GetNode<LineEdit>("%GridCols");
        _gridColCount.TextChanged += _ => GenerateGridTokens();
        _gridCardCount = GetNode<LineEdit>("%GridCardCount");
        _gridCardCount.TextChanged += _ => GenerateGridTokens();

        _gridSingleBack = GetNode<CheckButton>("%GridSingleBack");
        _gridSingleBack.Pressed += GenerateGridTokens;
    }

    private void InitializeMultiBindings()
    {
        for (int i = 0; i < MaxQuickSuitCount; i++)
        {
            _quickSuitColors[i] = GetNode<ColorPickerButton>($"%QuickSuit{i + 1}Color");
            _quickSuitValues[i] = GetNode<LineEdit>($"%QuickSuit{i + 1}Contents");
            _quickSuitRows[i] = GetNode<HBoxContainer>($"%QSRow{i + 1}");

            _quickSuitColors[i].ColorChanged += _ => GenerateQuickCards();
            _quickSuitValues[i].TextChanged += _ => GenerateQuickCards();
        }

        _quickSuitCount = GetNode<OptionButton>("%QuickSuitCount");
        _quickSuitCount.ItemSelected += QuickSuitCountChanged;

        _quickBackColor = GetNode<ColorPickerButton>("%QuickBackColor");
        _quickBackColor.ColorChanged += _ => GenerateQuickCards();

        _quickBackText = GetNode<LineEdit>("%QuickBackText");
        _quickBackText.TextChanged += _ => GenerateQuickCards();
    }

    private void QuickSuitCountChanged(long suitCount)
    {
        for (int i = 0; i < _quickSuitRows.Length; i++)
            _quickSuitRows[i].Visible = suitCount > i - 1;

        GenerateQuickCards();
    }

    private void LoadQuickSuits()
    {
        _quickSuits.Clear();
        _suitCount = _quickSuitCount.Selected + 1;
        for (int i = 0; i < _suitCount; i++)
        {
            _quickSuits.Add(
                new QuickCardData
                {
                    BackgroundColor = _quickSuitColors[i].Color,
                    Caption = _quickSuitValues[i].Text,
                    CardBackColor = _quickBackColor.Color,
                    CardBackValue = _quickBackText.Text,
                }
            );
        }
    }

    private void GenerateQuickCards()
    {
        _suitCount = _quickSuitCount.Selected + 1;
        _quickCards.Clear();

        for (int i = 0; i < _suitCount; i++)
        {
            var values = Utility.ParseValueRanges(_quickSuitValues[i].Text);
            foreach (var v in values)
            {
                _quickCards.Add(
                    new QuickCardData
                    {
                        BackgroundColor = _quickSuitColors[i].Color,
                        Caption = v,
                        CardBackColor = _quickBackColor.Color,
                        CardBackValue = _quickBackText.Text,
                    }
                );
            }
        }

        if (_tabs?.CurrentTab == 1)
        {
            _preview.ItemCount = _quickCards.Count;
            _preview.MultiItemMode = _quickCards.Count > 1;
            ChangePreviewToken(0);
        }
    }

    private string _frontGridImage;
    private string _backGridImage;

    private async void FrontImageSelected(object sender, SelectedEventArgs<Asset> e)
    {
        _frontGridImage = e.SelectedItem?.AssetId.ToString() ?? string.Empty;
        UpdatePreview();
    }

    private async void BackImageSelected(object sender, SelectedEventArgs<Asset> e)
    {
        _backGridImage = e.SelectedItem?.AssetId.ToString() ?? string.Empty;
        UpdatePreview();
    }

    private int _gridRows;
    private int _gridCols;
    private int _gridCount;

    private void GenerateGridTokens()
    {
        int.TryParse(_gridRowCount.Text, out _gridRows);
        int.TryParse(_gridColCount.Text, out _gridCols);
        int.TryParse(_gridCardCount.Text, out _gridCount);

        _preview.ItemCount = _gridCount;
        _preview.MultiItemMode = _gridCount > 1;
        ChangePreviewToken(0);
    }

    private void EditFrontTemplate() =>
        EventBus.Instance.Publish(
            new ShowTemplateEditor { TemplateRef = _frontTemplate?.Id ?? SnowTag.Empty }
        );

    private void EditBackTemplate() =>
        EventBus.Instance.Publish(
            new ShowTemplateEditor { TemplateRef = _backTemplate?.Id ?? SnowTag.Empty }
        );

    private void EditDataset() =>
        EventBus.Instance.Publish(
            new ShowDatasetEditor { DatasetRef = _textureContext.DataSet?.Id ?? SnowTag.Empty }
        );

    private TextureContext _textureContext = new();
    private Project _currentProject => ProjectService.Instance.CurrentProject;

    private void OnDatasetChanged(long index)
    {
        var datasetRef = new SnowTag(_datasetPicker.GetSelectedId());
        if (datasetRef == SnowTag.Empty)
        {
            _textureContext.DataSet = null;
            _textureContext.CurrentRowName = null;
            _preview.MultiItemMode = false;
        }
        else
        {
            _textureContext.DataSet = ProjectService.Instance.GetDataSet(datasetRef);
            _preview.MultiItemMode = true;
            _preview.SetItemLabels(_textureContext.DataSet.Rows.Keys.ToList());
        }

        UpdatePreview();
    }

    private Template _frontTemplate;
    private Template _backTemplate;

    private void OnFrontTemplateChanged(long index)
    {
        var templateRef = new SnowTag(_frontTemplatePicker.GetSelectedId());
        _frontTemplate =
            templateRef == SnowTag.Empty ? null : ProjectService.Instance.GetTemplate(templateRef);
        UpdatePreview();
    }

    private void OnBackTemplateChanged(long index)
    {
        var templateRef = new SnowTag(_backTemplatePicker.GetSelectedId());
        _backTemplate =
            templateRef == SnowTag.Empty ? null : ProjectService.Instance.GetTemplate(templateRef);
        UpdatePreview();
    }

    private void UpdateTemplateTab()
    {
        if (CurrentProject == null || _frontTemplatePicker == null)
            return;

        _frontTemplatePicker.Clear();
        _backTemplatePicker.Clear();
        _frontTemplatePicker.AddItem("(none)", SnowTag.Empty.Value);
        _backTemplatePicker.AddItem("(none)", SnowTag.Empty.Value);
        foreach (
            var t in CurrentProject.Templates.Where(x =>
                !x.Value.Deleted && x.Value.Target == Template.TemplateTarget.Flat
            )
        )
        {
            _frontTemplatePicker.AddItem(t.Value.Name, t.Key.Value);
            _backTemplatePicker.AddItem(t.Value.Name, t.Key.Value);
        }

        _datasetPicker.Clear();
        _datasetPicker.AddItem("(none)", SnowTag.Empty.Value);
        foreach (var d in CurrentProject.Datasets.Where(x => !x.Value.Deleted))
        {
            _datasetPicker.AddItem(d.Value.Name, d.Key.Value);
        }
    }

    public override void Activate()
    {
        var comp = GetPreviewComponent();
        _preview.SetComponent(comp, new Vector3(Mathf.DegToRad(90), 0, 0));
        UpdatePreview();
    }

    private VcToken GetPreviewComponent()
    {
        int shape = GetEffectiveShape();

        string sceneName = shape switch
        {
            1 => "VcTokenCircle.tscn",
            2 => "VcTokenHexPoint.tscn",
            3 => "VcTokenHexFlat.tscn",
            _ => "VcToken.tscn",
        };

        PrototypeIndex = shape;

        var scene = GD.Load<PackedScene>($"res://Scenes/VisualComponents/{sceneName}");
        var vc = scene.Instantiate<VcToken>();
        vc.Ready += UpdatePreview;
        return vc;
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    private void OnCustomBackCheckboxChange()
    {
        _customBackRow.Visible = _customBackCheckbox.ButtonPressed;
    }

    private void OnQuickBackCheckboxChange()
    {
        ShowQuickBack();
        UpdatePreview();
    }

    private void ShowQuickBack()
    {
        var h4 = GetNode<HBoxContainer>("%BottomBgContainer");
        h4.Visible = _quickBackCheckbox.ButtonPressed;
        _backField.Visible = _quickBackCheckbox.ButtonPressed;
    }

    private void GetFrontFile() =>
        ShowFileDialog(
            "Select Front Image File",
            file =>
            {
                _frontImage.Text = file;
                UpdatePreview();
            }
        );

    private void GetBackFile() =>
        ShowFileDialog(
            "Select Back Image File",
            file =>
            {
                _backImage.Text = file;
                UpdatePreview();
            }
        );

    public override ComponentParameters GetParams()
    {
        MultipleCreateMode = false;
        DataSet = null;

        int shape = GetEffectiveShape();

        float height = ParamToFloat(_heightInput.Text);
        float width = shape == 0 ? ParamToFloat(_widthInput.Text) : height;

        var d = new DeckParameters
        {
            ComponentName = _nameInput.Text,
            Height = height,
            Width = width,
            Thickness = ParamToFloat(_thicknessInput.Text),
            FrontImage = _frontImage.Text,
            BackImage = _backImage.Text,
            Shape = shape,
            FrontBgColor = _quickBackgroundColor.Color,
            Type = _typePicker.Selected switch
            {
                0 => VcToken.TokenType.Card,
                2 => VcToken.TokenType.Board,
                _ => VcToken.TokenType.Token,
            },
            FrontFontSize = 24,
        };

        bool spawnAsDeck = false;

        switch (_tabs.CurrentTab)
        {
            case 0:
                d.Mode = VcToken.TokenBuildMode.Quick;
                d.QuickFront = _frontField.GetQuickTextureField();
                d.QuickBack = _backField.GetQuickTextureField();
                d.DifferentBack = _quickBackCheckbox.ButtonPressed;
                break;

            case 1:
                LoadQuickSuits();
                d.QuickCardData = _quickSuits;
                d.DifferentBack = true;
                d.Mode = VcToken.TokenBuildMode.QuickDeck;
                spawnAsDeck = true;
                WidthHint = width / 10f;
                HeightHint = height / 10f;
                break;

            case 2:
                d.Mode = VcToken.TokenBuildMode.Custom;
                d.DifferentBack = _customBackCheckbox.ButtonPressed;
                break;

            case 3:
                d.FrontGridImageKey = _frontGridImage;
                d.BackGridImageKey = _backGridImage;
                d.GridRows = _gridRows;
                d.GridCols = _gridCols;
                d.GridCount = _gridCount;
                d.Mode = VcToken.TokenBuildMode.Grid;
                d.DifferentBack = true;
                d.GridSingleBack = _gridSingleBack.ButtonPressed;
                if (_gridCount > 1)
                {
                    spawnAsDeck = true;
                    WidthHint = width / 10f;
                    HeightHint = height / 10f;
                }
                break;

            case 4:
                d.Mode = VcToken.TokenBuildMode.Template;
                if (_frontTemplate != null)
                    d.FrontTemplate = _frontTemplate.Id;
                if (_backTemplate != null)
                    d.BackTemplate = _backTemplate.Id;
                d.Dataset = _textureContext.DataSet?.Id ?? SnowTag.Empty;
                if (_textureContext.DataSet != null)
                {
                    spawnAsDeck = true;
                    DataSet = ProjectService.Instance.GetDataSet(_textureContext.DataSet.Id);
                    WidthHint = width / 10f;
                    HeightHint = height / 10f;
                }
                break;
        }

        d.BackBgColor = _quickBackgroundColor2.Color;
        d.BackFontSize = 24;

        if (spawnAsDeck)
        {
            PrototypeIndex = 4;
            ComponentType = VisualComponentBase.VisualComponentType.Deck;
            return d;
        }

        PrototypeIndex = shape;
        ComponentType = VisualComponentBase.VisualComponentType.Token;
        return d.CloneAs<TokenParameters>();
    }

    private void UpdatePreview()
    {
        float h = ParamToFloat(_heightInput.Text);
        float w = GetEffectiveShape() == 0 ? ParamToFloat(_widthInput.Text) : h;
        float t = ParamToFloat(_thicknessInput.Text);
        if (h == 0 || w == 0 || t == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        // GetParams() also updates ComponentType (Token vs Deck) based on the active tab.
        _preview.Build(GetParams(), GetRow(_curToken), TextureFactory);
    }

    private string GetRow(int rowNum)
    {
        if (_tabs.CurrentTab == 1)
            return (rowNum + 1).ToString();
        if (_textureContext.DataSet == null)
            return rowNum.ToString();
        if (rowNum < 0 || rowNum >= _textureContext.DataSet.Rows.Count)
            return string.Empty;
        return _textureContext.DataSet.Rows.ElementAt(rowNum).Key;
    }

    private int _curToken;

    private void PreviewOnItemSelected(object sender, ItemSelectedEventArgs e) =>
        ChangePreviewToken(e.Index);

    private void ChangePreviewToken(int token)
    {
        _curToken = token;
        UpdatePreview();
    }

    public override void DisplayPrototype(SnowTag prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (PrintedParameters)prototype.Parameters;
        _suppressShapeReset = true;

        var storedType = p.Type;

        int storedShape = p.Shape;

        if (storedType == VcToken.TokenType.Card)
        {
            _typePicker.Select(0);
            OnTypeChanged(0);
            _shapePicker.Select(0);
        }
        else if (storedType == VcToken.TokenType.Board)
        {
            _typePicker.Select(2);
            OnTypeChanged(2);
            _shapePicker.Select(0);
        }
        else
        {
            _typePicker.Select(1);
            OnTypeChanged(1);
            (int shapeIdx, bool rotate) = storedShape switch
            {
                1 => (1, false),
                2 => (2, false),
                3 => (2, true),
                _ => (0, false),
            };
            _shapePicker.Select(shapeIdx);
            if (rotate)
                _landscapeButton.ButtonPressed = true;
            else
                _portraitButton.ButtonPressed = true;
        }

        _suppressShapeReset = false;

        _nameInput.Text = prototype.Name;
        _heightInput.Text = p.Height > 0 ? p.Height.ToString() : "";
        _widthInput.Text = p.Width > 0 ? p.Width.ToString() : "";
        _thicknessInput.Text = p.Thickness.ToString();
        _frontImage.Text = p.FrontImage;
        _backImage.Text = p.BackImage;

        _quickBackgroundColor.Color = p.FrontBgColor;
        _quickBackgroundColor2.Color = p.BackBgColor;

        _frontField.SetQuickTextureField(p.QuickFront);
        _backField.SetQuickTextureField(p.QuickBack);

        {
            bool differentBack = p.DifferentBack;
            _quickBackCheckbox.ButtonPressed = differentBack;
            _customBackCheckbox.ButtonPressed = differentBack;
            ShowQuickBack();
        }

        _tabs.CurrentTab = p.Mode switch
        {
            VcToken.TokenBuildMode.Quick => 0,
            VcToken.TokenBuildMode.QuickDeck => 1,
            VcToken.TokenBuildMode.Custom => 2,
            VcToken.TokenBuildMode.Grid => 3,
            VcToken.TokenBuildMode.Template => 4,
            _ => 0,
        };

        if (p.QuickCardData is { Count: > 0 } quickData)
        {
            _quickSuitCount.Select(Math.Min(quickData.Count - 1, MaxQuickSuitCount - 1));
            QuickSuitCountChanged(_quickSuitCount.Selected);
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

        _gridRowCount.Text = p.GridRows > 0 ? p.GridRows.ToString() : "";
        _gridColCount.Text = p.GridCols > 0 ? p.GridCols.ToString() : "";
        _gridCardCount.Text = p.GridCount > 0 ? p.GridCount.ToString() : "";
        int.TryParse(_gridRowCount.Text, out _gridRows);
        int.TryParse(_gridColCount.Text, out _gridCols);
        int.TryParse(_gridCardCount.Text, out _gridCount);
        _preview.ItemCount = _gridCount;

        _frontGridImage = p.FrontGridImageKey;
        var frontGridAsset = _currentProject?.Images.Values.FirstOrDefault(a =>
            a.AssetId.ToString() == _frontGridImage
        );
        _gridFrontImageSelector.SelectedImage = frontGridAsset;

        _backGridImage = p.BackGridImageKey;
        var backGridAsset = _currentProject?.Images.Values.FirstOrDefault(a =>
            a.AssetId.ToString() == _backGridImage
        );
        _gridBackImageSelector.SelectedImage = backGridAsset;

        _gridSingleBack.ButtonPressed = p.GridSingleBack;

        _frontTemplatePicker.Select(0);
        _frontTemplate = null;
        if (p.FrontTemplate != SnowTag.Empty)
        {
            var idx = _frontTemplatePicker.GetItemIndex(p.FrontTemplate.Value);
            if (idx >= 0)
            {
                _frontTemplatePicker.Select(idx);
                _frontTemplate = ProjectService.Instance.GetTemplate(p.FrontTemplate);
            }
        }

        _backTemplatePicker.Select(0);
        _backTemplate = null;
        if (p.BackTemplate != SnowTag.Empty)
        {
            var idx = _backTemplatePicker.GetItemIndex(p.BackTemplate.Value);
            if (idx >= 0)
            {
                _backTemplatePicker.Select(idx);
                _backTemplate = ProjectService.Instance.GetTemplate(p.BackTemplate);
            }
        }

        _datasetPicker.Select(0);
        _textureContext.DataSet = null;
        _textureContext.CurrentRowName = null;
        if (p.Dataset != SnowTag.Empty)
        {
            var idx = _datasetPicker.GetItemIndex(p.Dataset.Value);
            if (idx >= 0)
            {
                _datasetPicker.Select(idx);
                _textureContext.DataSet = ProjectService.Instance.GetDataSet(p.Dataset);
            }
        }

        UpdateDimensionUI();
        Activate();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as PrintedParameters;

        if (string.IsNullOrEmpty(p?.ComponentName))
            ret.Add("Name may not be blank");
        if ((p?.Height ?? 0) <= 0)
            ret.Add("Height must be > 0");
        if ((p?.Width ?? 0) <= 0)
            ret.Add("Width must be > 0");

        return ret;
    }
}

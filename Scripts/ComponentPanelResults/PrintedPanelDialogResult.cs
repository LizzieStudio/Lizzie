using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    private TemplateSelector _frontTemplatePicker;
    private Button _editFrontTemplateButton;
    private TemplateSelector _backTemplatePicker;
    private Button _editBackTemplateButton;
    private DataSetSelector _datasetPicker;
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

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        R.Get<Template>(_frontTemplateRef);
        R.Get<Template>(_backTemplateRef);

        var dataset = R.Get<DataSet>(_datasetRef);
        if (_tabs.CurrentTab == 4)
        {
            _preview.MultiItemMode = dataset != null;
            if (dataset != null)
                _preview.ItemCount = R.GetRows(_datasetRef).Count;
        }

        UpdatePreview();
    }

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
            else if (tab == 4)
                ProjectService.Instance.ForceSync(this);
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
        _frontTemplatePicker = GetNode<TemplateSelector>("%FrontTemplateList");
        _frontTemplatePicker.TemplateSelected += OnFrontTemplateChanged;
        _editFrontTemplateButton = GetNode<Button>("%EditFrontTemplateButton");
        _editFrontTemplateButton.Pressed += EditFrontTemplate;

        _backTemplatePicker = GetNode<TemplateSelector>("%BackTemplateList");
        _backTemplatePicker.TemplateSelected += OnBackTemplateChanged;
        _editBackTemplateButton = GetNode<Button>("%EditBackTemplateButton");
        _editBackTemplateButton.Pressed += EditBackTemplate;

        _datasetPicker = GetNode<DataSetSelector>("%DatasetList");
        _datasetPicker.DataSetSelected += OnDatasetChanged;

        _datasetEditorButton = GetNode<Button>("%EditDatasetButton");
        _datasetEditorButton.Pressed += EditDataset;
    }

    private void InitializeGridBindings()
    {
        _gridFrontImageSelector = GetNode<ImageSelector>("%FrontImageSelector");
        _gridFrontImageSelector.ImageSelected += FrontImageSelected;

        _gridBackImageSelector = GetNode<ImageSelector>("%BackImageSelector");
        _gridBackImageSelector.ImageSelected += BackImageSelected;

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

    private SnowTag _frontGridImage;
    private SnowTag _backGridImage;

    private async void FrontImageSelected(SnowTag Id)
    {
        _frontGridImage = Id;
        UpdatePreview();
    }

    private async void BackImageSelected(SnowTag Id)
    {
        _backGridImage = Id;
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
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateRef = _frontTemplateRef });

    private void EditBackTemplate() =>
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateRef = _backTemplateRef });

    private void EditDataset() =>
        EventBus.Instance.Publish(new ShowDatasetEditor { DatasetRef = _datasetRef });

    private SnowTag _datasetRef = SnowTag.Empty;

    private void OnDatasetChanged(SnowTag datasetRef)
    {
        _datasetRef = datasetRef;
        ProjectService.Instance.ForceSync(this);
    }

    private SnowTag _frontTemplateRef = SnowTag.Empty;
    private SnowTag _backTemplateRef = SnowTag.Empty;

    private void OnFrontTemplateChanged(SnowTag templateRef)
    {
        _frontTemplateRef = templateRef;
        ProjectService.Instance.ForceSync(this);
    }

    private void OnBackTemplateChanged(SnowTag templateRef)
    {
        _backTemplateRef = templateRef;
        ProjectService.Instance.ForceSync(this);
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
                d = d with
                {
                    Mode = VcToken.TokenBuildMode.Quick,
                    QuickFront = _frontField.GetQuickTextureField(),
                    QuickBack = _backField.GetQuickTextureField(),
                    DifferentBack = _quickBackCheckbox.ButtonPressed,
                };
                break;

            case 1:
                LoadQuickSuits();
                d = d with
                {
                    QuickCardData = _quickSuits.ToImmutableArray(),
                    DifferentBack = true,
                    Mode = VcToken.TokenBuildMode.QuickDeck,
                };
                spawnAsDeck = true;
                WidthHint = width / 10f;
                HeightHint = height / 10f;
                break;

            case 2:
                d = d with
                {
                    Mode = VcToken.TokenBuildMode.Custom,
                    DifferentBack = _customBackCheckbox.ButtonPressed,
                };
                break;

            case 3:
                d = d with
                {
                    FrontGridImageKey = _frontGridImage,
                    BackGridImageKey = _backGridImage,
                    GridRows = _gridRows,
                    GridCols = _gridCols,
                    GridCount = _gridCount,
                    Mode = VcToken.TokenBuildMode.Grid,
                    DifferentBack = true,
                    GridSingleBack = _gridSingleBack.ButtonPressed,
                };
                if (_gridCount > 1)
                {
                    spawnAsDeck = true;
                    WidthHint = width / 10f;
                    HeightHint = height / 10f;
                }
                break;

            case 4:
                d = d with { Mode = VcToken.TokenBuildMode.Template };
                d = d with
                {
                    FrontTemplate = _frontTemplateRef,
                    BackTemplate = _backTemplateRef,
                    Dataset = _datasetRef,
                };
                DataSet = ProjectService.Instance.Get<DataSet>(_datasetRef);
                if (DataSet != null)
                {
                    spawnAsDeck = true;
                    WidthHint = width / 10f;
                    HeightHint = height / 10f;
                }
                break;
        }

        d = d with { BackBgColor = _quickBackgroundColor2.Color, BackFontSize = 24 };

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

        var (rowIndex, rowId) = GetRow(_curToken);
        _preview.Build(GetParams(), rowIndex, rowId, TextureFactory);
    }

    private (int Index, SnowTag Id) GetRow(int rowNum)
    {
        if (_tabs.CurrentTab == 1 || _datasetRef == SnowTag.Empty)
            return (rowNum, SnowTag.Empty);
        var rows = ProjectService.Instance.GetRows(_datasetRef);
        if (rowNum < 0 || rowNum >= rows.Count)
            return (-1, SnowTag.Empty);
        return (-1, rows[rowNum].Id);
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
        var prototype = ProjectService.Instance.Prototypes.Records[prototypeId];
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

        if (p.QuickCardData is { Length: > 0 } quickData)
        {
            _quickSuitCount.Select(Math.Min(quickData.Length - 1, MaxQuickSuitCount - 1));
            QuickSuitCountChanged(_quickSuitCount.Selected);
            for (int i = 0; i < quickData.Length && i < MaxQuickSuitCount; i++)
            {
                _quickSuitColors[i].Color = quickData[i].BackgroundColor;
                _quickSuitValues[i].Text = quickData[i].Caption;
            }
            if (quickData.Length > 0)
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

        _gridFrontImageSelector.SetSelectedImage(p.FrontGridImageKey);

        _gridBackImageSelector.SetSelectedImage(p.BackGridImageKey);

        _gridSingleBack.ButtonPressed = p.GridSingleBack;

        _frontTemplatePicker.SelectedTemplate = p.FrontTemplate;
        _frontTemplateRef = p.FrontTemplate;

        _backTemplatePicker.SelectedTemplate = p.BackTemplate;
        _backTemplateRef = p.BackTemplate;

        _datasetPicker.SelectedDataSet = p.Dataset;
        _datasetRef = p.Dataset;
        ProjectService.Instance.ForceSync(this);

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

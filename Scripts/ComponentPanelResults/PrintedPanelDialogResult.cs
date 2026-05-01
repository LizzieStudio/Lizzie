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
    private OptionButton _cutPicker;
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

    private record CutPreset(string Label, int Shape, float W = 0f, float H = 0f, float T = 0f);

    private static readonly Dictionary<string, CutPreset[]> CutsByType = new()
    {
        ["Card"] = new[]
        {
            new CutPreset("Custom", 0),
            new CutPreset("Poker Card", 0, 63.5f, 88.9f, 0.2f),
            new CutPreset("Bridge Card", 0, 57.15f, 88.9f, 0.2f),
            new CutPreset("Mini Euro Card", 0, 44.45f, 63.5f, 0.2f),
            new CutPreset("Tarot Card", 0, 69.85f, 120.65f, 0.2f),
        },
        ["Token"] = new[]
        {
            new CutPreset("Rectangle", 0),
            new CutPreset("Circle", 1),
            new CutPreset("Hex", 2),
        },
        ["Board"] = new[]
        {
            new CutPreset("Quad Fold", 0, 254f, 254f, 2f),
            new CutPreset("Hex Fold", 0, 762f, 254f, 2f),
        },
    };

    private bool _suppressCutReset;

    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Token;

        _nameInput = GetNode<LineEdit>("%ItemName");

        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += _ =>
        {
            if (!_suppressCutReset)
                OnDimensionManualEdit();
            UpdatePreview();
        };

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += _ =>
        {
            if (!_suppressCutReset)
                OnDimensionManualEdit();
            UpdatePreview();
        };

        _thicknessInput = GetNode<LineEdit>("%Thickness");
        _thicknessInput.TextChanged += _ =>
        {
            if (!_suppressCutReset)
                OnDimensionManualEdit();
        };

        _typePicker = GetNode<OptionButton>("%TypePicker");
        _typePicker.ItemSelected += OnTypeChanged;

        _cutPicker = GetNode<OptionButton>("%CutPicker");
        _cutPicker.ItemSelected += OnCutChanged;

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
        _tabs.TabSelected += _ => UpdatePreview();

        _preview = GetNode<ComponentPreview>("%Preview");
        _preview.ItemSelected += PreviewOnItemSelected;

        InitializeTemplates();
        InitializeGridBindings();

        OnQuickBackCheckboxChange();
        OnCustomBackCheckboxChange();
        OnTypeChanged(0);
    }

    private string TypeKey() =>
        _typePicker.Selected switch
        {
            0 => "Card",
            1 => "Token",
            2 => "Board",
            _ => "Card",
        };

    private CutPreset GetCurrentCut()
    {
        var cuts = CutsByType[TypeKey()];
        var idx = Math.Clamp(_cutPicker.Selected, 0, cuts.Length - 1);
        return cuts[idx];
    }

    private int GetEffectiveShape()
    {
        var cut = GetCurrentCut();
        if (cut.Shape == 2 && _landscapeButton.ButtonPressed)
            return 3; // Hex Edge Up
        return cut.Shape;
    }

    private void OnTypeChanged(long _)
    {
        _cutPicker.Clear();
        foreach (var cut in CutsByType[TypeKey()])
            _cutPicker.AddItem(cut.Label);
        OnCutChanged(0);
    }

    private void OnCutChanged(long _)
    {
        var cut = GetCurrentCut();
        if (cut.W != 0f)
        {
            _suppressCutReset = true;
            _widthInput.Text = cut.W.ToString("f1");
            _heightInput.Text = cut.H.ToString("f1");
            _thicknessInput.Text = cut.T.ToString("f1");
            _suppressCutReset = false;
        }

        UpdateDimensionUI();

        if (Visible && !_suppressCutReset)
            Activate();
    }

    private void OnOrientationChanged()
    {
        if (_suppressCutReset)
            return;
        var cut = GetCurrentCut();
        if (cut.Shape == 0)
        {
            _suppressCutReset = true;
            (_widthInput.Text, _heightInput.Text) = (_heightInput.Text, _widthInput.Text);
            _suppressCutReset = false;
        }

        UpdatePreview();
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
    }

    private void OnDimensionManualEdit()
    {
        if (TypeKey() == "Card" && _cutPicker.ItemCount > 0)
            _cutPicker.Select(0);
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
        ChangePreviewToken(0);
    }

    private void EditFrontTemplate() =>
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateName = _frontTemplate?.Name });

    private void EditBackTemplate() =>
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateName = _backTemplate?.Name });

    private void EditDataset() =>
        EventBus.Instance.Publish(
            new ShowDatasetEditor { DatasetName = _textureContext.DataSet?.Name }
        );

    private TextureContext _textureContext = new();
    private Project _currentProject => ProjectService.Instance.CurrentProject;

    private void OnDatasetChanged(long index)
    {
        if (_datasetPicker.Selected == 0)
        {
            _textureContext.DataSet = null;
            _textureContext.CurrentRowName = null;
            _preview.MultiItemMode = false;
        }
        else
        {
            _textureContext.DataSet = _currentProject.Datasets[
                _datasetPicker.GetItemText((int)index)
            ];
            _preview.MultiItemMode = true;
            _preview.SetItemLabels(_textureContext.DataSet.Rows.Keys.ToList());
        }

        UpdatePreview();
    }

    private Template _frontTemplate;
    private Template _backTemplate;

    private void OnFrontTemplateChanged(long index)
    {
        _frontTemplate =
            _frontTemplatePicker.Selected == 0
                ? null
                : _currentProject.Templates[_frontTemplatePicker.GetItemText((int)index)];
        UpdatePreview();
    }

    private void OnBackTemplateChanged(long index)
    {
        _backTemplate =
            _backTemplatePicker.Selected == 0
                ? null
                : _currentProject.Templates[_backTemplatePicker.GetItemText((int)index)];
        UpdatePreview();
    }

    private void UpdateTemplateTab()
    {
        if (CurrentProject == null || _frontTemplatePicker == null)
            return;

        _frontTemplatePicker.Clear();
        _backTemplatePicker.Clear();
        _frontTemplatePicker.AddItem("(none)");
        _backTemplatePicker.AddItem("(none)");
        foreach (
            var t in CurrentProject.Templates.Where(x =>
                x.Value.Target == Template.TemplateTarget.Flat
            )
        )
        {
            _frontTemplatePicker.AddItem(t.Key);
            _backTemplatePicker.AddItem(t.Key);
        }

        _datasetPicker.Clear();
        _datasetPicker.AddItem("(none)");
        foreach (var d in CurrentProject.Datasets)
            _datasetPicker.AddItem(d.Key);
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

    public override List<string> Validity()
    {
        var ret = new List<string>();
        if (string.IsNullOrEmpty(_nameInput.Text.Trim()))
            ret.Add("Component Name required");
        return ret;
    }

    public override Dictionary<string, object> GetParams()
    {
        MultipleCreateMode = false;
        DataSet = null;

        var d = new Dictionary<string, object>();

        int shape = GetEffectiveShape();

        float height = ParamToFloat(_heightInput.Text);
        float width = shape == 0 ? ParamToFloat(_widthInput.Text) : height;

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", height);
        d.Add("Width", width);
        d.Add("Thickness", ParamToFloat(_thicknessInput.Text));
        d.Add("FrontImage", _frontImage.Text);
        d.Add("BackImage", _backImage.Text);
        d.Add("Shape", shape);
        d.Add("FrontBgColor", _quickBackgroundColor.Color);

        VcToken.TokenType tokenType = _typePicker.Selected switch
        {
            0 => VcToken.TokenType.Card,
            2 => VcToken.TokenType.Board,
            _ => VcToken.TokenType.Token,
        };
        d.Add("Type", tokenType);
        d.Add("FrontFontSize", 24);

        bool spawnAsDeck = false;

        switch (_tabs.CurrentTab)
        {
            case 0:
                d.Add("Mode", VcToken.TokenBuildMode.Quick);
                d.Add("QuickFront", _frontField.GetQuickTextureField());
                d.Add("QuickBack", _backField.GetQuickTextureField());
                d.Add("DifferentBack", _quickBackCheckbox.ButtonPressed);
                break;

            case 1:
                d.Add("Mode", VcToken.TokenBuildMode.Custom);
                d.Add("DifferentBack", _customBackCheckbox.ButtonPressed);
                break;

            case 2:
                d.Add("FrontGridImageKey", _frontGridImage);
                d.Add("BackGridImageKey", _backGridImage);
                d.Add("GridRows", _gridRows);
                d.Add("GridCols", _gridCols);
                d.Add("GridCount", _gridCount);
                d.Add("Mode", VcToken.TokenBuildMode.Grid);
                d.Add("DifferentBack", true);
                d.Add("GridSingleBack", _gridSingleBack.ButtonPressed);
                if (_gridCount > 1)
                {
                    spawnAsDeck = true;
                    WidthHint = width / 10f;
                    HeightHint = height / 10f;
                }
                break;

            case 3:
                d.Add("Mode", VcToken.TokenBuildMode.Template);
                if (_frontTemplate != null)
                    d.Add("FrontTemplate", _frontTemplate.Name);
                if (_backTemplate != null)
                    d.Add("BackTemplate", _backTemplate.Name);
                d.Add("Dataset", _textureContext.DataSet?.Name);
                if (_textureContext.DataSet != null)
                {
                    spawnAsDeck = true;
                    DataSet = ProjectService.Instance.GetDataSetByName(
                        _textureContext.DataSet?.Name
                    );
                    WidthHint = width / 10f;
                    HeightHint = height / 10f;
                }
                break;
        }

        d.Add("BackBgColor", _quickBackgroundColor2.Color);
        d.Add("BackFontSize", 24);

        if (spawnAsDeck)
        {
            PrototypeIndex = 4;
            ComponentType = VisualComponentBase.VisualComponentType.Deck;
        }
        else
        {
            PrototypeIndex = shape;
            ComponentType = VisualComponentBase.VisualComponentType.Token;
        }

        return d;
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

        var d = GetParams();
        _preview.Build(d, GetRow(_curToken), TextureFactory);
    }

    private string GetRow(int rowNum)
    {
        if (_textureContext.DataSet == null)
            return string.Empty;
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

    public override void DisplayPrototype(Guid prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        _suppressCutReset = true;

        var storedType =
            prototype.Parameters.TryGetValue("Type", out var typeObj)
            && typeObj is VcToken.TokenType tt
                ? tt
                : VcToken.TokenType.Token;

        int storedShape = prototype.Parameters.ContainsKey("Shape")
            ? Convert.ToInt32(prototype.Parameters["Shape"])
            : 0;

        if (storedType == VcToken.TokenType.Card)
        {
            _typePicker.Select(0);
            OnTypeChanged(0);
            _cutPicker.Select(0);
        }
        else if (storedType == VcToken.TokenType.Board)
        {
            _typePicker.Select(2);
            OnTypeChanged(2);
            _cutPicker.Select(0);
        }
        else
        {
            _typePicker.Select(1);
            OnTypeChanged(1);
            (int cutIdx, bool rotate) = storedShape switch
            {
                1 => (1, false),
                2 => (2, false),
                3 => (2, true),
                _ => (0, false),
            };
            _cutPicker.Select(cutIdx);
            if (rotate)
                _landscapeButton.ButtonPressed = true;
            else
                _portraitButton.ButtonPressed = true;
        }

        _suppressCutReset = false;

        _nameInput.Text = prototype.Name;
        _heightInput.Text = prototype.Parameters.GetValueOrDefault("Height", "")?.ToString() ?? "";
        _widthInput.Text = prototype.Parameters.GetValueOrDefault("Width", "")?.ToString() ?? "";
        if (prototype.Parameters.ContainsKey("Thickness"))
            _thicknessInput.Text = prototype.Parameters["Thickness"].ToString();
        if (prototype.Parameters.ContainsKey("FrontImage"))
            _frontImage.Text = prototype.Parameters["FrontImage"].ToString();
        if (prototype.Parameters.ContainsKey("BackImage"))
            _backImage.Text = prototype.Parameters["BackImage"].ToString();

        if (prototype.Parameters.ContainsKey("FrontBgColor"))
            _quickBackgroundColor.Color = (Color)prototype.Parameters["FrontBgColor"];
        if (prototype.Parameters.ContainsKey("BackBgColor"))
            _quickBackgroundColor2.Color = (Color)prototype.Parameters["BackBgColor"];

        if (prototype.Parameters.ContainsKey("QuickFront"))
            _frontField.SetQuickTextureField((QuickTextureField)prototype.Parameters["QuickFront"]);
        if (prototype.Parameters.ContainsKey("QuickBack"))
            _backField.SetQuickTextureField((QuickTextureField)prototype.Parameters["QuickBack"]);

        if (prototype.Parameters.ContainsKey("DifferentBack"))
        {
            bool differentBack = (bool)prototype.Parameters["DifferentBack"];
            _quickBackCheckbox.ButtonPressed = differentBack;
            _customBackCheckbox.ButtonPressed = differentBack;
            ShowQuickBack();
        }

        if (prototype.Parameters.ContainsKey("Mode"))
        {
            var mode = (VcToken.TokenBuildMode)prototype.Parameters["Mode"];
            _tabs.CurrentTab = mode switch
            {
                VcToken.TokenBuildMode.Quick => 0,
                VcToken.TokenBuildMode.Custom => 1,
                VcToken.TokenBuildMode.Grid => 2,
                VcToken.TokenBuildMode.Template => 3,
                _ => 0,
            };
        }

        UpdateDimensionUI();
        Activate();
    }
}

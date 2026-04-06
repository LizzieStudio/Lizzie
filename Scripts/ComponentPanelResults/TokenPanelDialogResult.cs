using System;
using System.Collections.Generic;
using System.IO;
using Godot;

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

    //private ColorPickerButton _quickTextColor2;
    //private LineEdit _quickText2;

    private CheckBox _quickBackCheckbox;
    private CheckBox _customBackCheckbox;

    private OptionButton _shapePicker;

    private TabContainer _tabs;

    private TextureRect _clipRect;
    private TextureRect _textureRect;
    private Label _label;

    private ComponentPreview _preview;
    private QuickTextureEntry _frontField;
    private QuickTextureEntry _backField;

    private IconLibrary _iconLibrary = new();

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

        _quickBackCheckbox = GetNode<CheckBox>("%ToggleBack");

        //TODO Restore to panel
        //_quickText.TextChanged += OnTextChange;
        //_quickTextColor.ColorChanged += OnPreviewTextColorChange;
        _frontField = GetNode<QuickTextureEntry>("%FrontField");
        _frontField.FieldChanged += (sender, args) => UpdatePreview();
        _frontField.SetIcons(_iconLibrary);

        _quickBackgroundColor.ColorChanged += OnBackgroundColorChanged;
        _quickBackCheckbox.Pressed += OnQuickBackCheckboxChange;

        _quickBackgroundColor2 = GetNode<ColorPickerButton>("%BottomBgColor");
        _quickBackgroundColor2.ColorChanged += OnBackgroundColor2Changed;

        _backField = GetNode<QuickTextureEntry>("%BackField");
        _backField.FieldChanged += (sender, args) => UpdatePreview();
        _backField.SetIcons(_iconLibrary);

        _shapePicker = GetNode<OptionButton>("%ShapePicker");
        _shapePicker.ItemSelected += ShapePickerOnItemSelected;

        _tabs = GetNode<TabContainer>("%Tabs");
        _tabs.TabSelected += OnTabSelected;

        _preview = GetNode<ComponentPreview>("%Preview");

        OnQuickBackCheckboxChange(); //just to set the initial line visibility in case someone messed with the control.
        OnCustomBackCheckboxChange();

        OnTabSelected(0);

        //ShapePickerOnItemSelected(0);
    }

    public override void Activate()
    {
        var comp = GetPreviewComponent();
        _preview.SetComponent(comp, new Vector3(Mathf.DegToRad(90), 0, 0));
        UpdatePreview();
    }

    private VcToken GetPreviewComponent()
    {
        string shape = "VcToken.tscn";

        /*
         * Square = 0,
        Circle = 1,
        HexPoint = 2,
        HexFlat = 3,
        RoundedRect = 4
         */
        switch (_shapePicker.Selected)
        {
            case 1:
                shape = "VcTokenCircle.tscn";
                break;

            case 2:
                shape = "VcTokenHexPoint.tscn";
                break;

            case 3:
                shape = "VcTokenHexFlat.tscn";
                break;
        }

        var scene = GD.Load<PackedScene>($"res://Scenes/VisualComponents/{shape}");
        var vc = scene.Instantiate<VcToken>();

        //This is the value used by the UI system to tell what component we want
        PrototypeIndex = _shapePicker.Selected;

        vc.Ready += UpdatePreview;
        return vc;
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    private void HeightWidthTextChanged(string newtext)
    {
        UpdatePreview();
    }

    private void OnCustomBackCheckboxChange()
    {
        _customBackRow.Visible = _customBackCheckbox.ButtonPressed;
    }

    private void OnTabSelected(long tab)
    {
        UpdatePreview();
    }

    private void ShapePickerOnItemSelected(long index)
    {
        Activate();
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

    private void OnPreviewTextColorChange(Color color)
    {
        UpdatePreview();
    }

    private void OnBackgroundColorChanged(Color color)
    {
        UpdatePreview();
    }

    private void OnTextChange(string newtext)
    {
        UpdatePreview();
    }

    private void OnPreviewTextColor2Change(Color color)
    {
        UpdatePreview();
    }

    private void OnBackgroundColor2Changed(Color color)
    {
        UpdatePreview();
    }

    private void OnText2Change(string newtext)
    {
        UpdatePreview();
    }

    private void GetFrontFile()
    {
        ShowFileDialog("Select Front Image File", FrontFileSelected);
    }

    private void FrontFileSelected(string file)
    {
        _frontImage.Text = file;
        UpdatePreview();
        return;

        if (!string.IsNullOrEmpty(file))
        {
            _frontImage.Text = file;
            if (File.Exists(_frontImage.Text))
            {
                var t = LoadTexture(_frontImage.Text);
                UpdatePreview();
            }
        }
    }

    private void GetBackFile()
    {
        ShowFileDialog("Select Back Image File", BackFileSelected);
    }

    private void BackFileSelected(string file)
    {
        _backImage.Text = file;
        UpdatePreview();
        return;

        if (!string.IsNullOrEmpty(file))
        {
            _backImage.Text = file;
            if (File.Exists(file))
            {
                var t = LoadTexture(file);
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
        d.Add("Mode", TabToBuildMode(_tabs.CurrentTab));
        d.Add("FrontBgColor", _quickBackgroundColor.Color);

        //TODO Replace with panel
        d.Add("QuickFront", _frontField.GetQuickTextureField());
        d.Add("QuickBack", _backField.GetQuickTextureField());

        d.Add("Type", VcToken.TokenType.Token);
        d.Add("FrontFontSize", 24);

        switch (_tabs.CurrentTab)
        {
            case 0:
                d.Add("DifferentBack", _quickBackCheckbox.ButtonPressed);
                break;

            case 1:
                d.Add("DifferentBack", _customBackCheckbox.ButtonPressed);
                break;

            case 2:
                d.Add("DifferentBack", false);
                break;
        }

        d.Add("BackBgColor", _quickBackgroundColor2.Color);
        d.Add("BackFontSize", 24);

        return d;
    }

    private VcToken.TokenBuildMode TabToBuildMode(int tab)
    {
        switch (tab)
        {
            case 0:
                return VcToken.TokenBuildMode.Quick;
            case 1:
                return VcToken.TokenBuildMode.Custom;
            case 2:
                return VcToken.TokenBuildMode.Grid;
        }

        throw new Exception("Unknown Tab Type in TokenPanelDialogResult");
    }

    private void UpdatePreview()
    {
        //normalize the size
        var h = ParamToFloat(_heightInput.Text);
        var w = ParamToFloat(_widthInput.Text);
        var t = ParamToFloat(_thicknessInput.Text);
        if (h == 0 || w == 0 || t == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        //normalize dimensions to 10x10x10 outer extants
        var scale = 10f / Math.Max(h, Math.Max(w, t));

        var d = new Dictionary<string, object>();

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", h * scale);
        d.Add("Width", w * scale);
        d.Add("Thickness", t * scale);
        d.Add("FrontImage", _frontImage.Text);
        d.Add("BackImage", _backImage.Text);
        d.Add("Shape", _shapePicker.Selected);
        d.Add("Mode", TabToBuildMode(_tabs.CurrentTab));
        d.Add("FrontBgColor", _quickBackgroundColor.Color);

        //TODO fix for panel
        //d.Add("FrontCaption", "");
        //d.Add("FrontCaptionColor", Colors.Black);
        d.Add("QuickFront", _frontField.GetQuickTextureField());
        d.Add("QuickBack", _backField.GetQuickTextureField());

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
        d.Add("BackFontSize", 24);

        _preview.Build(d, TextureFactory);
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
        _thicknessInput.Text = prototype.Parameters["Thickness"].ToString();
        _frontImage.Text = prototype.Parameters["FrontImage"].ToString();
        _backImage.Text = prototype.Parameters["BackImage"].ToString();
        _shapePicker.Select((int)prototype.Parameters["Shape"]);
        _quickBackgroundColor.Color = (Color)prototype.Parameters["FrontBgColor"];
        _quickBackgroundColor2.Color = (Color)prototype.Parameters["BackBgColor"];

        if (prototype.Parameters.ContainsKey("QuickFront"))
        {
            _frontField.SetQuickTextureField((QuickTextureField)prototype.Parameters["QuickFront"]);
        }

        if (prototype.Parameters.ContainsKey("QuickBack"))
        {
            _backField.SetQuickTextureField((QuickTextureField)prototype.Parameters["QuickBack"]);
        }

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
                _ => 0,
            };
        }

        Activate();
    }
}

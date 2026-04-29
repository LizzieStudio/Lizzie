using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Lizzie.AssetManagement;

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

    private OptionButton _frontTemplatePicker;
    private Button _editFrontTemplateButton;
    private OptionButton _backTemplatePicker;
    private Button _editBackTemplateButton;
    private OptionButton _datasetPicker;
    private Button _datasetEditorButton;

    private GridEntry _gridEntry;

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
        _preview.ItemSelected += PreviewOnItemSelected;

        InitializeTemplates();
        InitializeGridBindings();

        OnQuickBackCheckboxChange(); //just to set the initial line visibility in case someone messed with the control.
        OnCustomBackCheckboxChange();

        OnTabSelected(0);

        //ShapePickerOnItemSelected(0);
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
        _gridEntry = GetNode<GridEntry>("%GridEntry");
        _gridEntry.GridUpdated += OnGridUpdated;
        _gridEntry.CardCountUpdated += OnGridCardCountUpdate;
    }

    private void OnGridCardCountUpdate(object sender, EventArgs e)
    {
        _preview.ItemCount = _gridEntry.CardCount;
    }

    private void OnGridUpdated(object sender, EventArgs e)
    {
        UpdatePreview();
    }

    private string _frontGridImage;
    private string _backGridImage;

    private async void FrontImageSelected(object sender, SelectedEventArgs<Asset> e)
    {
        if (e.SelectedItem == null)
        {
            _frontGridImage = string.Empty;
            /*
            _frontMasterSprite = new ImageTexture(); //maybe set to blank white?
            UpdatePreview();
            return;
            */
        }

        {
            var a = e.SelectedItem;
            _frontGridImage = a.AssetId.ToString();
        }

        //ProjectService.Instance.FetchImageAsync(a, UpdateFrontGridTexture);
        UpdatePreview();
    }

    private async void BackImageSelected(object sender, SelectedEventArgs<Asset> e)
    {
        if (e.SelectedItem == null)
        {
            _backGridImage = string.Empty;
            /*
            _backMasterSprite = new ImageTexture(); //maybe set to blank white?
            UpdatePreview();
            return;
            */
        }
        else
        {
            var a = e.SelectedItem;
            _backGridImage = a.AssetId.ToString();
        }
        UpdatePreview();

        //ProjectService.Instance.FetchImageAsync(a, UpdateBackGridTexture);
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
        {
            _datasetPicker.AddItem(d.Key);
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
        MultipleCreateMode = false;
        DataSet = null;

        var d = new Dictionary<string, object>();

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", ParamToFloat(_heightInput.Text));
        d.Add("Width", ParamToFloat(_widthInput.Text));
        d.Add("Thickness", ParamToFloat(_thicknessInput.Text));
        d.Add("FrontImage", _frontImage.Text);
        d.Add("BackImage", _backImage.Text);
        d.Add("Shape", _shapePicker.Selected);
        d.Add("FrontBgColor", _quickBackgroundColor.Color);

        //TODO Replace with panel

        d.Add("Type", VcToken.TokenType.Token);
        d.Add("FrontFontSize", 24);

        switch (_tabs.CurrentTab)
        {
            //quick
            case 0:
                AddQuickParams(d);
                break;

            //custom
            case 1:
                d.Add("Mode", VcToken.TokenBuildMode.Custom);
                d.Add("DifferentBack", _customBackCheckbox.ButtonPressed);
                break;

            //grid
            case 2:
                AddGridParameters(d);
                break;

            //template
            case 3:
                AddTemplateParams(d);
                break;
        }

        d.Add("BackBgColor", _quickBackgroundColor2.Color);
        d.Add("BackFontSize", 24);

        return d;
    }

    private void AddQuickParams(Dictionary<string, object> d)
    {
        d.Add("Mode", VcToken.TokenBuildMode.Quick);
        d.Add("QuickFront", _frontField.GetQuickTextureField());
        d.Add("QuickBack", _backField.GetQuickTextureField());
        d.Add("DifferentBack", _quickBackCheckbox.ButtonPressed);
    }

    private void AddTemplateParams(Dictionary<string, object> d)
    {
        d.Add("Mode", VcToken.TokenBuildMode.Template);

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
            //d.Add("BackTemplate", _frontTemplate.Name);
        }

        d.Add("Dataset", _textureContext.DataSet?.Name);

        DataSet = ProjectService.Instance.GetDataSetByName(_textureContext.DataSet?.Name);
        MultipleCreateMode = (DataSet != null);
        WidthHint = ParamToFloat(_heightInput.Text) / 10f;
        HeightHint = ParamToFloat(_heightInput.Text) / 10f;
    }

    private void AddGridParameters(Dictionary<string, object> d)
    {
        _gridEntry.AddGridParameters(d);
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
            case 3:
                return VcToken.TokenBuildMode.Template;
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

    private void PreviewOnItemSelected(object sender, ItemSelectedEventArgs e)
    {
        ChangePreviewToken(e.Index);
    }

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
        _nameInput.Text = prototype.Name;
        _heightInput.Text = prototype.Parameters.ContainsKey("Height")
            ? prototype.Parameters["Height"].ToString()
            : "";
        _widthInput.Text = prototype.Parameters.ContainsKey("Width")
            ? prototype.Parameters["Width"].ToString()
            : "";
        _thicknessInput.Text = prototype.Parameters.ContainsKey("Thickness")
            ? prototype.Parameters["Thickness"].ToString()
            : "";
        _frontImage.Text = prototype.Parameters.ContainsKey("FrontImage")
            ? prototype.Parameters["FrontImage"].ToString()
            : "";
        _backImage.Text = prototype.Parameters.ContainsKey("BackImage")
            ? prototype.Parameters["BackImage"].ToString()
            : "";
        _shapePicker.Select(
            prototype.Parameters.ContainsKey("Shape") ? (int)prototype.Parameters["Shape"] : 0
        );
        _quickBackgroundColor.Color = prototype.Parameters.ContainsKey("FrontBgColor")
            ? (Color)prototype.Parameters["FrontBgColor"]
            : Colors.Black;
        _quickBackgroundColor2.Color = prototype.Parameters.ContainsKey("BackBgColor")
            ? (Color)prototype.Parameters["BackBgColor"]
            : Colors.Black;

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
                VcToken.TokenBuildMode.Template => 3,
                _ => 0,
            };
        }

        _gridEntry.UpdateGridControls(prototype.Parameters);

        // Template mode parameters
        _frontTemplatePicker.Select(0);
        _frontTemplate = null;
        if (prototype.Parameters.ContainsKey("FrontTemplate"))
        {
            string frontTemplateName = prototype.Parameters["FrontTemplate"].ToString();
            for (int i = 0; i < _frontTemplatePicker.ItemCount; i++)
            {
                if (_frontTemplatePicker.GetItemText(i) == frontTemplateName)
                {
                    _frontTemplatePicker.Select(i);
                    _frontTemplate = _currentProject.Templates.GetValueOrDefault(frontTemplateName);
                    break;
                }
            }
        }

        _backTemplatePicker.Select(0);
        _backTemplate = null;
        if (prototype.Parameters.ContainsKey("BackTemplate"))
        {
            string backTemplateName = prototype.Parameters["BackTemplate"].ToString();
            for (int i = 0; i < _backTemplatePicker.ItemCount; i++)
            {
                if (_backTemplatePicker.GetItemText(i) == backTemplateName)
                {
                    _backTemplatePicker.Select(i);
                    _backTemplate = _currentProject.Templates.GetValueOrDefault(backTemplateName);
                    break;
                }
            }
        }

        _datasetPicker.Select(0);
        _textureContext.DataSet = null;
        _textureContext.CurrentRowName = null;
        if (prototype.Parameters.ContainsKey("Dataset"))
        {
            string datasetName = prototype.Parameters["Dataset"]?.ToString();
            if (!string.IsNullOrEmpty(datasetName))
            {
                for (int i = 0; i < _datasetPicker.ItemCount; i++)
                {
                    if (_datasetPicker.GetItemText(i) == datasetName)
                    {
                        _datasetPicker.Select(i);
                        _textureContext.DataSet = _currentProject.Datasets.GetValueOrDefault(
                            datasetName
                        );
                        break;
                    }
                }
            }
        }

        Activate();
    }
}

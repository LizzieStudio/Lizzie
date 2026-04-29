using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class DiePanelDialogResult : ComponentPanelDialogResult
{
    private LineEdit _nameInput;
    private LineEdit _diameterInput;
    private OptionButton _sidesInput;
    private ColorPickerButton _dieColor;

    private TabContainer _tabContainer;
    private ComponentPreview _preview;

    private OptionButton _frontTemplatePicker;
    private Button _editFrontTemplateButton;

    private OptionButton _datasetPicker;
    private Button _datasetEditorButton;

    [Export]
    private QuickTextureEntry[] _quickSideEntries;

    private IconLibrary _iconLibrary = new();

    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Die;
        _nameInput = GetNode<LineEdit>("%ComponentName");
        _diameterInput = GetNode<LineEdit>("%Diameter");
        _diameterInput.TextChanged += text => UpdatePreview();

        _sidesInput = GetNode<OptionButton>("%Sides");
        _sidesInput.ItemSelected += SidesInputOnItemSelected;

        InitializeTemplates();

        _dieColor = GetNode<ColorPickerButton>("%DieColor");
        _dieColor.ColorChanged += color => UpdatePreview();

        _preview = GetNode<ComponentPreview>("%Preview");
        _preview.ItemSelected += PreviewOnItemSelected;

        _tabContainer = GetNode<TabContainer>("%TabContainer");
        _tabContainer.CurrentTab = 0;
        _tabContainer.TabChanged += t => UpdatePreview();

        int i = 0;
        foreach (var l in _quickSideEntries)
        {
            l.TextValue = (i + 1).ToString();
            l.FieldChanged += (sender, args) => UpdatePreview();
            l.SetIcons(_iconLibrary);
            i++;
        }

        PrototypeIndex = 1;
        UpdateQuickSidesVisibility();

        //register for events
        EventBus.Instance.Subscribe<TemplateChangedEvent>(TemplateChanged);
        EventBus.Instance.Subscribe<DataSetChangedEvent>(DataSetChanged);
    }

    private int _curDie;

    private void PreviewOnItemSelected(object sender, ItemSelectedEventArgs e)
    {
        _curDie = e.Index;
        UpdatePreview();
    }

    private void TemplateChanged(TemplateChangedEvent obj)
    {
        UpdatePreview();
    }

    private void DataSetChanged(DataSetChangedEvent obj)
    {
        UpdatePreview();
    }

    private void InitializeTemplates()
    {
        _frontTemplatePicker = GetNode<OptionButton>("%FrontTemplateList");
        _frontTemplatePicker.ItemSelected += OnFrontTemplateChanged;
        _editFrontTemplateButton = GetNode<Button>("%EditFrontTemplateButton");
        _editFrontTemplateButton.Pressed += EditFrontTemplate;

        _datasetPicker = GetNode<OptionButton>("%DatasetList");
        _datasetPicker.ItemSelected += OnDatasetChanged;

        _datasetEditorButton = GetNode<Button>("%EditDatasetButton");
        _datasetEditorButton.Pressed += EditDataset;

        UpdateTemplateTab();
    }

    private void UpdateTemplateTab()
    {
        if (CurrentProject == null || _frontTemplatePicker == null)
            return;

        _frontTemplatePicker.Clear();

        _frontTemplatePicker.AddItem("(none)");

        int.TryParse(_sidesInput.Text, out var sides);
        var target = SidesToTarget(sides);

        foreach (var t in CurrentProject.Templates.Where(x => x.Value.Target == target))
        {
            _frontTemplatePicker.AddItem(t.Key);
        }

        _datasetPicker.Clear();
        _datasetPicker.AddItem("(none)");
        foreach (var d in CurrentProject.Datasets)
        {
            _datasetPicker.AddItem(d.Key);
        }
    }

    private Template.TemplateTarget SidesToTarget(int sides)
    {
        switch (sides)
        {
            case 4:
                return Template.TemplateTarget.D4;
            case 6:
                return Template.TemplateTarget.D6;
            case 8:
                return Template.TemplateTarget.D8;
            case 10:
                return Template.TemplateTarget.D10;
            case 12:
                return Template.TemplateTarget.D12;
            case 20:
                return Template.TemplateTarget.D20;
        }

        return Template.TemplateTarget.D6;
    }

    private TextureContext _textureContext = new();

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
            _textureContext.DataSet = ProjectService.Instance.CurrentProject.Datasets[
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
            _frontTemplate = ProjectService.Instance.CurrentProject.Templates[
                _frontTemplatePicker.GetItemText((int)index)
            ];
        }

        UpdatePreview();
    }

    private void EditFrontTemplate()
    {
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateName = _frontTemplate?.Name });
    }

    private void EditDataset()
    {
        EventBus.Instance.Publish(
            new ShowDatasetEditor { DatasetName = _textureContext.DataSet?.Name }
        );
    }

    public override void Activate()
    {
        var comp = GetPreviewComponent();
        _preview.SetComponent(comp, new Vector3(Mathf.DegToRad(-45), 0, 0));
        UpdatePreview();
    }

    private VcDie GetPreviewComponent()
    {
        string shape = "VcD6s.tscn";

        switch (_sidesInput.Selected)
        {
            case 0:
                shape = "vc_d_4.tscn";
                break;

            case 1:
                shape = "VcD6s.tscn";
                break;

            case 2:
                shape = "VcD8.tscn";
                break;

            case 3:
                shape = "VcD10.tscn";
                break;

            case 4:
                shape = "VcD12.tscn";
                break;

            case 5:
                shape = "VcD20.tscn";
                break;
        }

        var scene = GD.Load<PackedScene>($"res://Scenes/VisualComponents/Dice/{shape}");
        var vc = scene.Instantiate<VcDie>();

        vc.Ready += UpdatePreview;
        return vc;
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    private void SidesInputOnItemSelected(long index)
    {
        UpdateQuickSidesVisibility();
        PrototypeIndex = (int)index;
        Activate();
    }

    private void UpdateQuickSidesVisibility()
    {
        if (_quickSideEntries.Length < 20)
            return;

        if (int.TryParse(_sidesInput.Text, out var target))
        {
            for (int i = 0; i < 20; i++)
            {
                _quickSideEntries[i].Visible = (i < target);
            }
        }
    }

    public override List<string> Validity()
    {
        return new List<string>();
    }

    public override Dictionary<string, object> GetParams()
    {
        var d = new Dictionary<string, object>();

        MultipleCreateMode = false;
        DataSet = null;

        d.Add("ComponentName", _nameInput.Text);

        var dia = ParamToFloat(_diameterInput.Text);
        d.Add("Size", dia);
        d.Add("Color", _dieColor.Color);
        if (int.TryParse(_sidesInput.Text, out var sides))
        {
            d.Add("SideCount", sides);
        }

        switch (_tabContainer.CurrentTab)
        {
            case 0:
                d.Add("Mode", VcToken.TokenBuildMode.Quick);
                d.Add("Sides", PackageSides());
                break;

            case 1: //Custom
                d.Add("Mode", VcToken.TokenBuildMode.Custom);

                break;

            case 2:
                d.Add("Mode", VcToken.TokenBuildMode.Template);

                if (_frontTemplate != null)
                {
                    d.Add("FrontTemplate", _frontTemplate.Name);
                }

                d.Add("Dataset", _textureContext.DataSet?.Name);

                DataSet = ProjectService.Instance.GetDataSetByName(_textureContext.DataSet?.Name);
                MultipleCreateMode = (DataSet != null);
                WidthHint = dia / 10;
                HeightHint = dia / 10;

                break;
        }

        return d;
    }

    private QuickTextureField[] PackageSides()
    {
        if (!int.TryParse(_sidesInput.Text, out var sides))
            return Array.Empty<QuickTextureField>();

        var s = new QuickTextureField[sides];

        for (int i = 0; i < sides; i++)
        {
            s[i] = _quickSideEntries[i].GetQuickTextureField();
        }

        return s;
    }

    private void UpdatePreview()
    {
        //normalize the size
        var dia = ParamToFloat(_diameterInput.Text);
        if (dia == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        dia = 16;

        _preview.SetComponentVisibility(true);

        var d = GetParams();

        _preview.Build(d, GetRow(_curDie), TextureFactory);
    }

    private string GetRow(int rowNum)
    {
        if (_textureContext.DataSet == null)
            return string.Empty;
        if (rowNum < 0 || rowNum >= _textureContext.DataSet.Rows.Count)
            return string.Empty;
        return _textureContext.DataSet.Rows.ElementAt(rowNum).Key;
    }

    public override void DisplayPrototype(Guid prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        _nameInput.Text = prototype.Name;
        _diameterInput.Text = prototype.Parameters.ContainsKey("Size")
            ? prototype.Parameters["Size"].ToString()
            : "";
        _dieColor.Color = prototype.Parameters.ContainsKey("Color")
            ? (Color)prototype.Parameters["Color"]
            : Colors.Black;

        if (
            prototype.Parameters.ContainsKey("Sides")
            && prototype.Parameters["Sides"] is QuickTextureField[] sides
        )
        {
            for (int i = 0; i < sides.Length && i < _quickSideEntries.Length; i++)
            {
                _quickSideEntries[i].SetQuickTextureField(sides[i]);
            }

            // Set the sides dropdown based on array length
            int sideIndex = sides.Length switch
            {
                4 => 0,
                6 => 1,
                8 => 2,
                10 => 3,
                12 => 4,
                20 => 5,
                _ => 1,
            };
            _sidesInput.Select(sideIndex);
            SidesInputOnItemSelected(sideIndex);
        }
        else if (prototype.Parameters.ContainsKey("SideCount"))
        {
            int sideCount = (int)prototype.Parameters["SideCount"];
            int sideIndex = sideCount switch
            {
                4 => 0,
                6 => 1,
                8 => 2,
                10 => 3,
                12 => 4,
                20 => 5,
                _ => 1,
            };
            _sidesInput.Select(sideIndex);
            SidesInputOnItemSelected(sideIndex);
        }

        // Restore tab/mode
        if (prototype.Parameters.ContainsKey("Mode"))
        {
            var mode = (VcToken.TokenBuildMode)prototype.Parameters["Mode"];
            _tabContainer.CurrentTab = mode switch
            {
                VcToken.TokenBuildMode.Quick => 0,
                VcToken.TokenBuildMode.Custom => 1,
                VcToken.TokenBuildMode.Template => 2,
                _ => 0,
            };
        }
        else
        {
            _tabContainer.CurrentTab = 0;
        }

        // Restore template
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
                    _frontTemplate =
                        ProjectService.Instance.CurrentProject.Templates.GetValueOrDefault(
                            frontTemplateName
                        );
                    break;
                }
            }
        }

        // Restore dataset
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
                        _textureContext.DataSet =
                            ProjectService.Instance.CurrentProject.Datasets.GetValueOrDefault(
                                datasetName
                            );
                        break;
                    }
                }
            }
        }
    }
}

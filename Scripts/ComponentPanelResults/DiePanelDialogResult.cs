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
        if (TemplateManager.Instance != null)
            TemplateManager.Instance.TemplatesChanged += TemplatesChanged;
        if (DataSetManager.Instance != null)
            DataSetManager.Instance.DataSetsChanged += DataSetsChanged;
    }

    private int _curDie;

    private void PreviewOnItemSelected(object sender, ItemSelectedEventArgs e)
    {
        _curDie = e.Index;
        UpdatePreview();
    }

    private void TemplatesChanged(int[] ids)
    {
        UpdatePreview();
    }

    private void DataSetsChanged(int[] ids)
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
        _frontTemplatePicker.AddItem("(none)", SnowTag.Empty.Value);

        int.TryParse(_sidesInput.Text, out var sides);
        var target = SidesToTarget(sides);

        foreach (
            var t in CurrentProject.Templates.Where(x =>
                !x.Value.Deleted && x.Value.Target == target
            )
        )
        {
            _frontTemplatePicker.AddItem(t.Value.Name, t.Key.Value);
        }

        _datasetPicker.Clear();
        _datasetPicker.AddItem("(none)", SnowTag.Empty.Value);
        foreach (var d in CurrentProject.Datasets.Where(x => !x.Value.Deleted))
        {
            _datasetPicker.AddItem(d.Value.Name, d.Key.Value);
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

    private void OnFrontTemplateChanged(long index)
    {
        var templateRef = new SnowTag(_frontTemplatePicker.GetSelectedId());
        if (templateRef == SnowTag.Empty)
        {
            _frontTemplate = null;
        }
        else
        {
            _frontTemplate = ProjectService.Instance.GetTemplate(templateRef);
        }

        UpdatePreview();
    }

    private void EditFrontTemplate()
    {
        EventBus.Instance.Publish(
            new ShowTemplateEditor { TemplateRef = _frontTemplate?.Id ?? SnowTag.Empty }
        );
    }

    private void EditDataset()
    {
        EventBus.Instance.Publish(
            new ShowDatasetEditor { DatasetRef = _textureContext.DataSet?.Id ?? SnowTag.Empty }
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

    public override ComponentParameters GetParams()
    {
        MultipleCreateMode = false;
        DataSet = null;

        var dia = ParamToFloat(_diameterInput.Text);

        var p = new DieParameters
        {
            ComponentName = _nameInput.Text,
            Size = dia,
            Color = _dieColor.Color,
        };

        if (int.TryParse(_sidesInput.Text, out var sides))
        {
            p.SideCount = sides;
        }

        switch (_tabContainer.CurrentTab)
        {
            case 0:
                p.Mode = VcToken.TokenBuildMode.Quick;
                p.Sides = PackageSides();
                break;

            case 1:
                p.Mode = VcToken.TokenBuildMode.Custom;
                break;

            case 2:
                p.Mode = VcToken.TokenBuildMode.Template;

                if (_frontTemplate != null)
                {
                    p.FrontTemplate = _frontTemplate.Id;
                }

                p.Dataset = _textureContext.DataSet?.Id ?? SnowTag.Empty;

                DataSet = ProjectService.Instance.GetDataSet(
                    _textureContext.DataSet?.Id ?? SnowTag.Empty
                );
                MultipleCreateMode = (DataSet != null);
                WidthHint = dia / 10;
                HeightHint = dia / 10;

                break;
        }

        return p;
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

        _preview.Build(GetParams(), GetRow(_curDie), TextureFactory);
    }

    private string GetRow(int rowNum)
    {
        if (_textureContext.DataSet == null)
            return string.Empty;
        if (rowNum < 0 || rowNum >= _textureContext.DataSet.Rows.Count)
            return string.Empty;
        return _textureContext.DataSet.Rows.ElementAt(rowNum).Key;
    }

    public override void DisplayPrototype(SnowTag prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (DieParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _diameterInput.Text = p.Size.ToString();
        _dieColor.Color = p.Color;

        if (p.Sides != null && p.Sides.Length > 0)
        {
            var sides = p.Sides;
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
        else if (p.SideCount > 0)
        {
            int sideCount = p.SideCount;
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
        _tabContainer.CurrentTab = p.Mode switch
        {
            VcToken.TokenBuildMode.Quick => 0,
            VcToken.TokenBuildMode.Custom => 1,
            VcToken.TokenBuildMode.Template => 2,
            _ => 0,
        };

        // Restore template
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

        // Restore dataset
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
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as DieParameters;

        if (string.IsNullOrEmpty(p?.ComponentName))
            ret.Add("Name may not be blank");
        if ((p?.Size ?? 0) <= 0)
            ret.Add("Diameter must be > 0");

        return ret;
    }
}

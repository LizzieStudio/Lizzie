using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

    private TemplateSelector _frontTemplatePicker;
    private Button _editFrontTemplateButton;

    private DataSetSelector _datasetPicker;
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
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        R.Get<Template>(_frontTemplateRef);

        var dataset = R.Get<DataSet>(_datasetRef);
        _preview.MultiItemMode = dataset != null;
        if (dataset != null)
            _preview.ItemCount = R.GetRows(_datasetRef).Count;

        UpdatePreview();
    }

    private int _curDie;

    private void PreviewOnItemSelected(object sender, ItemSelectedEventArgs e)
    {
        _curDie = e.Index;
        UpdatePreview();
    }

    private void InitializeTemplates()
    {
        _frontTemplatePicker = GetNode<TemplateSelector>("%FrontTemplateList");
        _frontTemplatePicker.TemplateSelected += OnFrontTemplateChanged;
        _editFrontTemplateButton = GetNode<Button>("%EditFrontTemplateButton");
        _editFrontTemplateButton.Pressed += EditFrontTemplate;

        _datasetPicker = GetNode<DataSetSelector>("%DatasetList");
        _datasetPicker.DataSetSelected += OnDatasetChanged;

        _datasetEditorButton = GetNode<Button>("%EditDatasetButton");
        _datasetEditorButton.Pressed += EditDataset;

        UpdateTemplateTarget();
    }

    private void UpdateTemplateTarget()
    {
        int.TryParse(_sidesInput.Text, out var sides);
        var target = SidesToTarget(sides);
        _frontTemplatePicker.Target = target;

        var template = ProjectService.Instance.Get<Template>(_frontTemplateRef);
        if (template != null && template.Target != target)
        {
            _frontTemplateRef = SnowTag.Empty;
            _frontTemplatePicker.SelectedTemplate = SnowTag.Empty;
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

    private SnowTag _datasetRef = SnowTag.Empty;

    private void OnDatasetChanged(SnowTag datasetRef)
    {
        _datasetRef = datasetRef;
        ProjectService.Instance.ForceSync(this);
    }

    private SnowTag _frontTemplateRef = SnowTag.Empty;

    private void OnFrontTemplateChanged(SnowTag templateRef)
    {
        _frontTemplateRef = templateRef;
        ProjectService.Instance.ForceSync(this);
    }

    private void EditFrontTemplate()
    {
        EventBus.Instance.Publish(new ShowTemplateEditor { TemplateRef = _frontTemplateRef });
    }

    private void EditDataset()
    {
        EventBus.Instance.Publish(new ShowDatasetEditor { DatasetRef = _datasetRef });
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
        UpdateTemplateTarget();
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
            p = p with { SideCount = sides };
        }

        switch (_tabContainer.CurrentTab)
        {
            case 0:
                p = p with { Mode = VcToken.TokenBuildMode.Quick, Sides = PackageSides() };
                break;

            case 1:
                p = p with { Mode = VcToken.TokenBuildMode.Custom };
                break;

            case 2:
                p = p with { Mode = VcToken.TokenBuildMode.Template };

                p = p with { FrontTemplate = _frontTemplateRef, Dataset = _datasetRef };

                DataSet = ProjectService.Instance.Get<DataSet>(_datasetRef);
                MultipleCreateMode = (DataSet != null);
                WidthHint = dia / 10;
                HeightHint = dia / 10;

                break;
        }

        return p;
    }

    private ImmutableArray<QuickTextureField> PackageSides()
    {
        if (!int.TryParse(_sidesInput.Text, out var sides))
            return ImmutableArray<QuickTextureField>.Empty;

        var s = new QuickTextureField[sides];

        for (int i = 0; i < sides; i++)
        {
            s[i] = _quickSideEntries[i].GetQuickTextureField();
        }

        return s.ToImmutableArray();
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

        _preview.SetComponentVisibility(true);

        var rowId = GetRow(_curDie);
        _preview.Build(GetParams(), -1, rowId, TextureFactory);
    }

    private SnowTag GetRow(int rowNum)
    {
        var rows = ProjectService.Instance.GetRows(_datasetRef);
        if (rowNum < 0 || rowNum >= rows.Count)
            return SnowTag.Empty;
        return rows[rowNum].Id;
    }

    public override void DisplayPrototype(SnowTag prototypeId)
    {
        var prototype = ProjectService.Instance.Prototypes.Records[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (DieParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _diameterInput.Text = p.Size.ToString();
        _dieColor.Color = p.Color;

        if (p.Sides.Length > 0)
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
        _frontTemplatePicker.SelectedTemplate = p.FrontTemplate;
        _frontTemplateRef = p.FrontTemplate;

        // Restore dataset
        _datasetPicker.SelectedDataSet = p.Dataset;
        _datasetRef = p.Dataset;
        ProjectService.Instance.ForceSync(this);
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

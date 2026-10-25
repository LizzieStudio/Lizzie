using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class DiePanelDialogResult : ComponentPanelDialogResult
{
    private LineEdit _nameInput;
    private LineEdit _diameterInput;
    private OptionButton _sidesInput;
    private ColorPickerButton _dieColor;
    private SideEditor _faces;
    private ComponentPreview _preview;

    // The scene the preview shows, or null while the panel is inactive.
    private string _previewScene;

    public override void _Ready()
    {
        _nameInput = GetNode<LineEdit>("%ComponentName");
        _diameterInput = GetNode<LineEdit>("%Diameter");
        _diameterInput.TextChanged += _ => Refresh();

        _sidesInput = GetNode<OptionButton>("%Sides");
        _sidesInput.ItemSelected += _ =>
        {
            ShowTemplates();
            Refresh();
        };

        _dieColor = GetNode<ColorPickerButton>("%DieColor");
        _dieColor.ColorChanged += _ => Refresh();

        _faces = GetNode<SideEditor>("%Faces");
        _faces.Changed += Refresh;
        _faces.Side = new QuickSide { Suits = [new QuickSuit { Values = "1-6" }] };
        ShowTemplates();

        _preview = GetNode<ComponentPreview>("%Preview");
        _preview.ItemSelected += (_, _) => UpdatePreview();
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    private void Refresh() => Atoms.R.QueueSync(this);

    private int SideCount => _sidesInput.GetSelectedId();

    private void ShowTemplates()
    {
        int sides = SideCount;
        _faces.ShowsTemplates = t => sides == 0 ? t.Sides > 0 : t.Sides == sides;
    }

    private void Sync(IWatchReader R)
    {
        var p = Parameters();
        _sidesInput.SetItemText(0, $"Auto (d{(p with { SideCount = 0 }).Resolve(R).Sides})");

        int count = p.Faces is TemplateSide side ? side.Rows(R).Count : 1;
        _preview.ItemCount = count;
        _preview.MultiItemMode = count > 1;

        if (_previewScene != null && _previewScene != Utility.DieScene(p, R))
            Activate();
        else
            UpdatePreview();
    }

    public override void Activate()
    {
        _previewScene = Utility.DieScene(Parameters(), Atoms.R);
        var die = GD.Load<PackedScene>(_previewScene).Instantiate<VcDie>();
        _preview.SetComponent(die, new Vector3(Mathf.DegToRad(-45), 0, 0));
        UpdatePreview();
    }

    public override void Deactivate()
    {
        _previewScene = null;
        _preview.ClearComponent();
    }

    private DieParameters Parameters() =>
        new()
        {
            ComponentName = _nameInput.Text,
            Size = ParamToFloat(_diameterInput.Text),
            Color = _dieColor.Color,
            SideCount = SideCount,
            Faces = _faces.Side,
        };

    public override ComponentParameters GetParams() => Parameters();

    private void UpdatePreview()
    {
        var p = Parameters();
        if (p.Size == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        var rows = (p.Faces as TemplateSide)?.Rows(Atoms.R) ?? [];
        var row = rows.ElementAtOrDefault(_preview.CurrentItem);
        _preview.Build(p, new DieItemRef(row), TextureFactory);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (DieParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _diameterInput.Text = p.Size.ToString();
        _dieColor.Color = p.Color;
        _sidesInput.Select(_sidesInput.GetItemIndex(p.SideCount));
        ShowTemplates();
        _faces.Side = p.Faces;
        Activate();
        Refresh();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = (DieParameters)parameters;

        if (string.IsNullOrEmpty(p.ComponentName))
            ret.Add("Name may not be blank");
        if (p.Size <= 0)
            ret.Add("Diameter must be > 0");
        if (p.Resolve(Atoms.R).Error is { } error)
            ret.Add(error);

        return ret;
    }
}

using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class TrayPanelDialogResult : ComponentPanelDialogResult
{
    private LineEdit _nameInput;
    private LineEdit _heightInput;
    private LineEdit _widthInput;
    private LineEdit _lengthInput;
    private ColorPickerButton _colorPicker;
    private ComponentPreview _preview;
    private AtomSelector _prototypeList;
    private readonly WatchableValue<Ref<Prototype>> _selectedPrototypeRef = new();

    public override void _Ready()
    {
        _nameInput = GetNode<LineEdit>("%ItemName");
        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += t => UpdatePreview();

        _lengthInput = GetNode<LineEdit>("%Length");
        _lengthInput.TextChanged += t => UpdatePreview();

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += t => UpdatePreview();

        _colorPicker = GetNode<ColorPickerButton>("%Color");
        _colorPicker.ColorChanged += ColorPickerOnColorChanged;

        _prototypeList = GetNode<AtomSelector>("%PrototypeList");
        _prototypeList.List(
            R => R.Get(Prototype.Live).Select(p => (p.Id, p.Name)),
            new(
                R => R.Get(_selectedPrototypeRef),
                id =>
                {
                    _selectedPrototypeRef.Value = id;
                    UpdatePreview();
                }
            )
        );

        _preview = GetNode<ComponentPreview>("%Preview");
    }

    private void ColorPickerOnColorChanged(Color color)
    {
        UpdatePreview();
    }

    public override void Activate()
    {
        _preview.SetComponent(GetPreviewComponent(), new Vector3(Mathf.DegToRad(30), 0, 0));
        UpdatePreview();
    }

    private VcTray GetPreviewComponent()
    {
        var scene = GD.Load<PackedScene>("res://Scenes/VisualComponents/VcTray.tscn");
        return scene.Instantiate<VcTray>();
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    public override string PrototypeName => _nameInput.Text;

    public override ComponentParameters GetParams()
    {
        return new TrayParameters
        {
            Height = ParamToFloat(_heightInput.Text),
            Width = ParamToFloat(_widthInput.Text),
            Length = ParamToFloat(_lengthInput.Text),
            Color = _colorPicker.Color,
            DispenseRef = _selectedPrototypeRef.Value,
        };
    }

    private void UpdatePreview()
    {
        //normalize the size
        var h = ParamToFloat(_heightInput.Text);
        var w = ParamToFloat(_widthInput.Text);
        var l = ParamToFloat(_lengthInput.Text);

        if (h == 0 || w == 0 || l == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        var p = new TrayParameters
        {
            Height = h,
            Width = w,
            Length = l,
            Color = _colorPicker.Color,
            DispenseRef = _selectedPrototypeRef.Value,
        };
        _preview.Build(p);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (TrayParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _heightInput.Text = p.Height.ToString();
        _widthInput.Text = p.Width.ToString();
        _lengthInput.Text = p.Length.ToString();
        _colorPicker.Color = p.Color;

        _selectedPrototypeRef.Value = p.DispenseRef;

        Activate();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as TrayParameters;

        if ((p?.Height ?? 0) <= 0)
            ret.Add("Height must be > 0");
        if ((p?.Width ?? 0) <= 0)
            ret.Add("Width must be > 0");
        if ((p?.Length ?? 0) <= 0)
            ret.Add("Length must be > 0");
        if (p?.DispenseRef.IsEmpty ?? true)
            ret.Add("Component must be specified");

        return ret;
    }
}

using System;
using System.Collections.Generic;
using Godot;

public partial class CubePanelDialogResult : ComponentPanelDialogResult
{
    private LineEdit _nameInput;
    private LineEdit _heightInput;
    private LineEdit _widthInput;
    private LineEdit _lengthInput;
    private ColorPickerButton _colorPicker;
    private ComponentPreview _preview;

    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Cube;
        _nameInput = GetNode<LineEdit>("%ItemName");
        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += t => UpdatePreview();

        _lengthInput = GetNode<LineEdit>("%Length");
        _lengthInput.TextChanged += t => UpdatePreview();

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += t => UpdatePreview();

        _colorPicker = GetNode<ColorPickerButton>("%Color");
        _colorPicker.ColorChanged += ColorPickerOnColorChanged;
        _preview = GetNode<ComponentPreview>("%Preview");
    }

    private void ColorPickerOnColorChanged(Color color)
    {
        UpdatePreview();
    }

    private bool _subviewportInitComplete;
    private int _subViewportFrames = 3;

    public override void Activate()
    {
        _preview.SetComponent(GetPreviewComponent(), new Vector3(Mathf.DegToRad(-10), 0, 0));
        UpdatePreview();
    }

    private VcCube GetPreviewComponent()
    {
        var scene = GD.Load<PackedScene>("res://Scenes/VisualComponents/VcCube.tscn");
        return scene.Instantiate<VcCube>();
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    public override ComponentParameters GetParams()
    {
        return new CubeParameters
        {
            ComponentName = _nameInput.Text,
            Height = ParamToFloat(_heightInput.Text),
            Width = ParamToFloat(_widthInput.Text),
            Length = ParamToFloat(_lengthInput.Text),
            Color = _colorPicker.Color,
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

        var p = new CubeParameters
        {
            ComponentName = _nameInput.Text,
            Height = h,
            Width = w,
            Length = l,
            Color = _colorPicker.Color,
        };

        _preview.Build(p, TextureFactory);
    }

    public override void DisplayPrototype(Guid prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (CubeParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _heightInput.Text = p.Height.ToString();
        _widthInput.Text = p.Width.ToString();
        _lengthInput.Text = p.Length.ToString();
        _colorPicker.Color = p.Color;

        Activate();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as CubeParameters;

        if (string.IsNullOrEmpty(p?.ComponentName))
            ret.Add("Name may not be blank");
        if ((p?.Height ?? 0) <= 0)
            ret.Add("Height must be > 0");
        if ((p?.Width ?? 0) <= 0)
            ret.Add("Width must be > 0");
        if ((p?.Length ?? 0) <= 0)
            ret.Add("Length must be > 0");

        return ret;
    }
}

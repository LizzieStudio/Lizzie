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

    public override Dictionary<string, object> GetParams()
    {
        var d = new Dictionary<string, object>();

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", ParamToFloat(_heightInput.Text));
        d.Add("Width", ParamToFloat(_widthInput.Text));
        d.Add("Length", ParamToFloat(_lengthInput.Text));
        d.Add("Color", _colorPicker.Color);

        return d;
    }

    private void UpdatePreview()
    {
        var d = new Dictionary<string, object>();

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

        //normalize dimensions to 10x10x10 outer extants
        //var scale = 10f / Math.Max(h, Math.Max(w, l));
        var scale = 1;

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", h * scale);
        d.Add("Width", w * scale);
        d.Add("Length", l * scale);
        d.Add("Color", _colorPicker.Color);

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
        _heightInput.Text = prototype.Parameters.ContainsKey("Height")
            ? prototype.Parameters["Height"].ToString()
            : "";
        _widthInput.Text = prototype.Parameters.ContainsKey("Width")
            ? prototype.Parameters["Width"].ToString()
            : "";
        _lengthInput.Text = prototype.Parameters.ContainsKey("Length")
            ? prototype.Parameters["Length"].ToString()
            : "";
        _colorPicker.Color = prototype.Parameters.ContainsKey("Color")
            ? (Color)prototype.Parameters["Color"]
            : Colors.Red;

        Activate();
    }

    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
    {
        var ret = new List<string>();

        //must have a name and height. Width/length optional
        if (parameters.ContainsKey("ComponentName"))
        {
            if (string.IsNullOrEmpty(parameters["ComponentName"].ToString()))
                ret.Add("Name may not be blank");
        }
        else
        {
            ret.Add("Instance Name not included");
        }

        var h = Utility.GetParam<float>(parameters, "Height");
        if (h <= 0)
            ret.Add("Height must be > 0");


        var w = Utility.GetParam<float>(parameters, "Width");
        if (w <= 0)
            ret.Add("Width must be > 0");


        var l = Utility.GetParam<float>(parameters, "Length");
        if (l <= 0)
            ret.Add("Length must be > 0");


        return ret;
    }
}

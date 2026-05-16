using System;
using System.Collections.Generic;
using Godot;

public partial class BagPanelDialogResults : ComponentPanelDialogResult
{
    private LineEdit _nameInput;
    private LineEdit _heightInput;
    private LineEdit _diameterInput;

    private ColorPickerButton _colorPicker;
    private ComponentPreview _preview;

    private Button _showCountButton;

    public override void _Ready()
    {
        ComponentType = VisualComponentBase.VisualComponentType.Bag;
        _nameInput = GetNode<LineEdit>("%ItemName");
        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += t => UpdatePreview();

        _diameterInput = GetNode<LineEdit>("%Diameter");
        _diameterInput.TextChanged += t => UpdatePreview();

        _colorPicker = GetNode<ColorPickerButton>("%Color");
        _colorPicker.ColorChanged += color => UpdatePreview();

        _showCountButton = GetNode<Button>("%ShowCountButton");
        _showCountButton.Pressed += UpdatePreview;

        _preview = GetNode<ComponentPreview>("%Preview");
    }

    public override void _Process(double delta)
    {
        //_previewDisc.Rotation += new Vector3(0,(float)delta, 0);
    }

    public override void Activate()
    {
        _preview.SetComponent(GetPreviewComponent(), new Vector3(Mathf.DegToRad(-10), 0, 0));
        UpdatePreview();
    }

    private VcBag GetPreviewComponent()
    {
        var scene = GD.Load<PackedScene>("res://Scenes/VisualComponents/VcBag.tscn");
        return scene.Instantiate<VcBag>();
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
        d.Add("Diameter", ParamToFloat((_diameterInput).Text));
        d.Add("Color", _colorPicker.Color);
        d.Add("ShowCount", _showCountButton.ButtonPressed);

        return d;
    }

    private void UpdatePreview()
    {
        var d = new Dictionary<string, object>();

        var h = ParamToFloat(_heightInput.Text);
        var dia = ParamToFloat(_diameterInput.Text);

        if (h == 0 || dia == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        //normalize dimensions to 10x10x10 outer extants
        var scale = 10f / Math.Max(h, dia);

        d.Add("ComponentName", _nameInput.Text);
        d.Add("Height", h * scale);
        d.Add("Diameter", dia * scale);
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
        _diameterInput.Text = prototype.Parameters.ContainsKey("Diameter")
            ? prototype.Parameters["Diameter"].ToString()
            : "";
        _colorPicker.Color = prototype.Parameters.ContainsKey("Color")
            ? (Color)prototype.Parameters["Color"]
            : Colors.Red;

        _showCountButton.ButtonPressed = Utility.GetParam<bool>(prototype.Parameters, "ShowCount");

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


        var w = Utility.GetParam<float>(parameters, "Diameter");
        if (w <= 0)
            ret.Add("Diameter must be > 0");



        return ret;
    }
}

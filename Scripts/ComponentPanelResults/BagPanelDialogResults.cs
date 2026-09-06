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

    public override ComponentParameters GetParams()
    {
        return new BagParameters
        {
            ComponentName = _nameInput.Text,
            Height = ParamToFloat(_heightInput.Text),
            Diameter = ParamToFloat(_diameterInput.Text),
            Color = _colorPicker.Color,
            ShowCount = _showCountButton.ButtonPressed,
        };
    }

    private void UpdatePreview()
    {
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

        var p = new BagParameters
        {
            ComponentName = _nameInput.Text,
            Height = h * scale,
            Diameter = dia * scale,
            Color = _colorPicker.Color,
        };

        _preview.Build(p, TextureFactory);
    }

    public override void DisplayPrototype(SnowportId prototypeId)
    {
        var prototype = ProjectService.Instance.CurrentProject.Prototypes[prototypeId];
        DisplayPrototype(prototype);
    }

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (BagParameters)prototype.Parameters;
        _nameInput.Text = prototype.Name;
        _heightInput.Text = p.Height.ToString();
        _diameterInput.Text = p.Diameter.ToString();
        _colorPicker.Color = p.Color;
        _showCountButton.ButtonPressed = p.ShowCount;

        Activate();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as BagParameters;

        if (string.IsNullOrEmpty(p?.ComponentName))
            ret.Add("Name may not be blank");
        if ((p?.Height ?? 0) <= 0)
            ret.Add("Height must be > 0");
        if ((p?.Diameter ?? 0) <= 0)
            ret.Add("Diameter must be > 0");

        return ret;
    }
}

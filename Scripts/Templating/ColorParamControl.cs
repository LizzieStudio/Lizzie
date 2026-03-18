using System;
using Godot;
using Lizzie.Scripts.Templating;

public partial class ColorParamControl : HBoxContainer, IParamControl
{
    private Label _label;
    private LineEdit _value;
    private Button _script;
    private ColorPickerButton _colorPicker;

    private TemplateParameter _parameter;
    private bool _readyComplete;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _label = GetNode<Label>("Caption");
        _value = GetNode<LineEdit>("LineEdit");
        _script = GetNode<Button>("Formula");
        _colorPicker = GetNode<ColorPickerButton>("ColorPickerButton");
        _colorPicker.ColorChanged += OnColorChanged;

        if (_parameter != null)
        {
            MapParam();
        }

        _value.TextChanged += RaiseParameterUpdated;

        _readyComplete = true;
    }

    private void OnColorChanged(Color color)
    {
        _parameter.Value = color.ToHtml();
        _value.Text = _parameter.Value;
        RaiseParameterUpdated(_parameter.Value);
    }

    private void MapParam()
    {
        _label.Text = _parameter.Name;
        _value.Text = _parameter.Value.ToString();
        _colorPicker.Color = new Color(_parameter.Value);
    }

    public void SetParameter(TemplateParameter parameter)
    {
        _parameter = parameter;
        if (_readyComplete)
        {
            MapParam();
        }
    }

    public void UpdateParameter(string newValue)
    {
        _parameter.Value = newValue;
        _value.Text = _parameter.Value;
    }

    public TemplateParameter GetParameter()
    {
        _parameter.Value = _value.Text;
        return _parameter;
    }

    public event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;

    private void RaiseParameterUpdated(string value)
    {
        _parameter.Value = value;
        ParameterUpdated?.Invoke(this, new TemplateParamUpdateEventArgs { Parameter = _parameter });
    }
}

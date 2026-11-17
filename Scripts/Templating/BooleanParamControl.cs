using System;
using Godot;

namespace Lizzie;

public partial class BooleanParamControl : HBoxContainer, IParamControl
{
    // Called when the node enters the scene tree for the first time.
    private Label _label;
    private CheckButton _value;
    private LineEdit _text;

    private TemplateParameter _parameter;
    private bool _readyComplete;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _label = GetNode<Label>("Caption");
        _value = GetNode<CheckButton>("CheckButton");

        _text = GetNode<LineEdit>("LineEdit");
        _text.TextChanged += text =>
        {
            _parameter.Value = text;
            _value.ButtonPressed = TemplateElement.IsTruthy(text);
            RaiseParameterUpdated();
        };

        if (_parameter != null)
        {
            MapParam();
        }

        _value.Pressed += OnOptionSelected;

        _readyComplete = true;
    }

    private void OnOptionSelected()
    {
        _parameter.Value = _value.ButtonPressed.ToString();
        _text.Text = _parameter.Value;

        RaiseParameterUpdated();
    }

    private void MapParam()
    {
        _label.Text = _parameter.Name;
        _text.Text = _parameter.Value;
        _value.ButtonPressed = TemplateElement.IsTruthy(_parameter.Value);
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
        MapParam();
    }

    public TemplateParameter GetParameter() => _parameter;

    public event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;

    private void RaiseParameterUpdated()
    {
        ParameterUpdated?.Invoke(
            this,
            new TemplateParamUpdateEventArgs { Parameter = GetParameter() }
        );
    }
}

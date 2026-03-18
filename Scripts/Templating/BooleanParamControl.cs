using System;
using Godot;
using Lizzie.Scripts.Templating;

public partial class BooleanParamControl : HBoxContainer, IParamControl
{
    // Called when the node enters the scene tree for the first time.
    private Label _label;
    private CheckButton _value;
    private Button _script;
    private LineEdit _text;

    private TemplateParameter _parameter;
    private bool _readyComplete;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _label = GetNode<Label>("Caption");
        _value = GetNode<CheckButton>("CheckButton");

        _text = GetNode<LineEdit>("LineEdit");
        _text.TextChanged += _ => RaiseParameterUpdated();

        _script = GetNode<Button>("Formula");

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
        _text.Text = _value.ButtonPressed ? "Y" : "N";

        RaiseParameterUpdated();
    }

    private bool _initializing;

    private void MapParam()
    {
        _initializing = true;

        _label.Text = _parameter.Name;
        if (bool.TryParse(_parameter.Value, out bool value))
        {
            _value.ButtonPressed = value;
            _text.Text = value ? "Y" : "N";
        }
        else
        {
            _value.ButtonPressed = false;
            _text.Text = "N";
        }

        _initializing = false;
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
        _parameter.Value = _text.Text;
        return _parameter;
    }

    public event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;

    private void RaiseParameterUpdated()
    {
        if (_initializing)
            return;
        ParameterUpdated?.Invoke(
            this,
            new TemplateParamUpdateEventArgs { Parameter = GetParameter() }
        );
    }
}

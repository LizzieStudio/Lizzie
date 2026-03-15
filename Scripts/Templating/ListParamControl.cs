using System;
using System.ComponentModel.Design.Serialization;
using Godot;
using Lizzie.Scripts.Templating;

public partial class ListParamControl : HBoxContainer, IParamControl
{
    // Called when the node enters the scene tree for the first time.
    private Label _label;
    private LineEdit _value;
    private Button _script;
    private OptionButton _listPicker;

    private TemplateParameter _parameter;
    private bool _readyComplete;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _label = GetNode<Label>("Caption");
        _value = GetNode<LineEdit>("LineEdit");
        _script = GetNode<Button>("Formula");
        _listPicker = GetNode<OptionButton>("OptionButton");
        _listPicker.ItemSelected += OnOptionSelected;

        if (_parameter != null)
        {
            MapParam();
        }

        _value.TextChanged += RaiseParameterUpdated;

        _readyComplete = true;
    }

    private void OnOptionSelected(long index)
    {
        _parameter.Value = _listPicker.GetItemText((int)index);
        _value.Text = _parameter.Value;
        _listPicker.Text = string.Empty;
        RaiseParameterUpdated(_parameter.Value);
    }

    private bool _initializing;

    private void MapParam()
    {
        _initializing = true;

        _label.Text = _parameter.Name;
        _value.Text = _parameter.Value.ToString();

        for (int i = 0; i < _listPicker.GetItemCount(); i++)
        {
            if (_listPicker.GetItemText(i) == _parameter.Value)
            {
                _listPicker.Select(i);
            }
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
        _parameter.Value = _value.Text;
        return _parameter;
    }

    public event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;

    private void RaiseParameterUpdated(string value)
    {
        if (_initializing)
            return;
        _parameter.Value = value;
        ParameterUpdated?.Invoke(this, new TemplateParamUpdateEventArgs { Parameter = _parameter });
    }
}

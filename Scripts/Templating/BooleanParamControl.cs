using Godot;
using System;
using TTSS.Scripts.Templating;

public partial class BooleanParamControl : HBoxContainer, IParamControl
{
// Called when the node enters the scene tree for the first time.
	private Label _label;
	private CheckButton _value;
	private Button _script;
	
	private TemplateParameter _parameter;
	private bool _readyComplete;

// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_label = GetNode<Label>("Caption");
		_value = GetNode<CheckButton>("CheckButton");
		
		_script = GetNode<Button>("Formula");
		
		
		if (_parameter != null)
		{
			MapParam();
		}

		_value.Pressed += RaiseParameterUpdated;

		_readyComplete = true;
	}

	private void OnOptionSelected(long index)
	{
		_parameter.Value = _value.ButtonPressed.ToString();
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
		}
		else
		{
			_value.ButtonPressed = false;
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
		_parameter.Value = _value.ButtonPressed.ToString();
		return _parameter;
	}

	public event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;

	private void RaiseParameterUpdated()
	{
		if (_initializing) return;
		ParameterUpdated?.Invoke(this, new TemplateParamUpdateEventArgs { Parameter = GetParameter() });
	}
}

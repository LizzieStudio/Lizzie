using Godot;
using System;
using TTSS.Scripts.Templating;

public partial class NumericParamControl : HBoxContainer, IParamControl
{
    private Label _label;
    private LineEdit _value;
    private Button _script;

    private TemplateParameter _parameter;
    private bool _readyComplete;
    
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_label = GetNode<Label>("Caption");
		_value = GetNode<LineEdit>("LineEdit");
		_script = GetNode<Button>("Formula");
		
		if (_parameter != null)
		{
			MapParam();
		}
		
		_value.TextChanged += RaiseParameterUpdated;
		
		_readyComplete = true;
	}

	private void MapParam()
	{
		_label.Text = _parameter.Name;
		_value.Text = _parameter.Value.ToString();
	}
	
	public void SetParameter(TemplateParameter parameter)
	{
		_parameter = parameter;
		if (_readyComplete)
		{
			MapParam();
		}
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
		ParameterUpdated?.Invoke(this, new TemplateParamUpdateEventArgs {Parameter = _parameter});
	}
	
}

public class TemplateParamUpdateEventArgs : EventArgs
{
	public TemplateParameter Parameter { get; set; }
}

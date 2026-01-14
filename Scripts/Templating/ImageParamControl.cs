using Godot;
using System;
using TTSS.Scripts.Templating;

public partial class ImageParamControl : HBoxContainer, IParamControl
{
	private Label _label;
	private LineEdit _value;
	private ColorPickerButton _colorPicker;
	private MenuButton _iconList;
	private PopupMenu _popupMenu;
	
	private OptionButton _qtyPicker;
	private TemplateParameter _parameter;
	private bool _readyComplete;


	private bool _initializing;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_initializing = true;
		
		_label = GetNode<Label>("Caption");
		_value = GetNode<LineEdit>("LineEdit");

		_iconList = GetNode<MenuButton>("%ShapeList");
		_popupMenu = _iconList.GetPopup();
		_popupMenu.IndexPressed += OnOptionSelected;
		
		_value.TextChanged += RaiseParameterUpdated;
		
		_readyComplete = true;
		if (_icons != null) SetIcons(_icons);
		MapParam();
		
		_initializing = false;
	}

	private IconLibrary _icons;

	public IconLibrary IconLibrary
	{
		get => _icons; 
		set {_icons = value; if (_readyComplete) SetIcons(_icons); }
}

	public void SetIcons(IconLibrary icons)
	{
		icons.LoadPopupMenu(_popupMenu);
	}
	




	private string _fieldName;
	
	private void MapParam()
	{
		_initializing = true;
		
		_label.Text = _parameter.Name;
		_value.Text = _parameter.Value.ToString();
		
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

	private void OnOptionSelected(long index)
	{
		string value = string.Empty;
		value = _popupMenu.GetItemText((int)index);
		

		
		
		_parameter.Value = value;
		_value.Text = _parameter.Value;
		RaiseParameterUpdated(_parameter.Value);
	}
	
	private void RaiseParameterUpdated(string value)
	{
		if (_initializing) return;
		_parameter.Value = value;
		ParameterUpdated?.Invoke(this, new TemplateParamUpdateEventArgs { Parameter = _parameter });
	}
	
	public event EventHandler<TemplateParamUpdateEventArgs> ParameterUpdated;
}



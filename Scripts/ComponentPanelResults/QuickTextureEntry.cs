using Godot;
using System;

public partial class QuickTextureEntry : BoxContainer
{
	private Label _fieldCaption;
	private LineEdit _text;
	private ColorPickerButton _colorPicker;
	private OptionButton _optionTypes;
	private OptionButton _coreIcons;
	private OptionButton _extendedIcons;
	private OptionButton _userIcons;

	private bool _initializing;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_initializing = true;
		
		_fieldCaption = GetNode<Label>("%Label");
		_fieldCaption.Text = _fieldName;
		
		_optionTypes = GetNode<OptionButton>("%OptionButton");
		_optionTypes.Selected = 0;
		_optionTypes.ItemSelected += TypeChanged;
		
		_coreIcons = GetNode<OptionButton>("%ShapeList");
		_coreIcons.ItemSelected += _ => RaiseFieldChanged();
		
		_extendedIcons = GetNode<OptionButton>("%IconList");
		_extendedIcons.ItemSelected += _ => RaiseFieldChanged();
		
		_userIcons = GetNode<OptionButton>("%UserIconList");
		_userIcons.ItemSelected += _ => RaiseFieldChanged();

		_colorPicker = GetNode<ColorPickerButton>("%TopTextColor");
		_colorPicker.Color = Colors.Black;
		_colorPicker.ColorChanged += _ => RaiseFieldChanged();
		
		_text = GetNode<LineEdit>("%TopCaption");
		_text.TextChanged += _ => RaiseFieldChanged();
		
		UpdateVisibility(0);
		
		_initializing = false;
	}

	private IconLibrary _icons;

	public void SetIcons(IconLibrary icons)
	{
		_icons = icons;
		_icons.LoadOptionButtonCore(_coreIcons);
		_icons.LoadOptionButtonExtended(_extendedIcons);
	}
	
	private void TypeChanged(long index)
	{
		UpdateVisibility(index);
		RaiseFieldChanged();
	}

	private void UpdateVisibility(long index)
	{
		_text.Visible = (index == 0);
		_coreIcons.Visible = (index == 1);
		_extendedIcons.Visible = (index == 2);
		_userIcons.Visible = (index == 3);
	}

	private string _fieldName;
	
	[Export]
	public string FieldCaption
	{
		get => _fieldName;
		set 
		{
			_fieldName = value;
			if (_fieldCaption == null) return;
			_fieldCaption.Text = value; 
		}
	}

	public string TextValue
	{
		get => _text.Text;
		set => _text.Text = value;
	}


	private void RaiseFieldChanged()
	{
		if (_initializing) return;
		
		var qt = GetQuickTextureField();
		
		FieldChanged?.Invoke(this, new QuickTextureFieldEventArgs(qt));
	}
	
	public event EventHandler<QuickTextureFieldEventArgs> FieldChanged;

	public QuickTextureField GetQuickTextureField()
	{
		var qt = new QuickTextureField
		{
			
			ForegroundColor = _colorPicker.Color,
		};

		switch (_optionTypes.Selected)
		{
			case 0:
				qt.FaceType = TextureFactory.TextureObjectType.Text;
				qt.Caption = _text.Text;
				break;
			
			case 1:
				qt.FaceType = TextureFactory.TextureObjectType.CoreShape;
				qt.Caption = _coreIcons.GetItemText((int)_coreIcons.Selected);
				break;
			
			case 2:
				qt.FaceType = TextureFactory.TextureObjectType.ExtendedShape;
				qt.Caption = _extendedIcons.GetItemText((int)_extendedIcons.Selected);
				break;
			
			case 3:
				qt.FaceType = TextureFactory.TextureObjectType.UserShape;
				qt.Caption = _userIcons.GetItemText((int)_userIcons.Selected);
				break;
			
			
		}

		return qt;
	}
}

public class QuickTextureFieldEventArgs : EventArgs
{
	public QuickTextureFieldEventArgs(QuickTextureField field)
	{
		QuickTextureField = field;
	}
	public QuickTextureField QuickTextureField { get; set; }
}

public class QuickTextureField
{
	public TextureFactory.TextureObjectType FaceType { get; set; }
	public Color ForegroundColor { get; set; }
	public string Caption { get; set; }
}

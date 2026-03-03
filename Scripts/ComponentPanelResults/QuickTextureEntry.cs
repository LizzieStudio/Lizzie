using Godot;
using System;

public partial class QuickTextureEntry : BoxContainer
{
	private Label _fieldCaption;
	private LineEdit _text;
	private ColorPickerButton _colorPicker;
	private OptionButton _optionTypes;
	private OptionButton _iconList;
	private OptionButton _qtyPicker;

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
		
		_iconList = GetNode<OptionButton>("%ShapeList");
		_iconList.ItemSelected += IconSelected;
		
		_colorPicker = GetNode<ColorPickerButton>("%TopTextColor");
		_colorPicker.Color = Colors.Black;
		_colorPicker.ColorChanged += _ => RaiseFieldChanged();
		
		_text = GetNode<LineEdit>("%TopCaption");
		_text.TextChanged += _ => RaiseFieldChanged();
		
		_qtyPicker = GetNode<OptionButton>("%Qty");
		_qtyPicker.ItemSelected += _ => RaiseFieldChanged();
		_qtyPicker.Hide();
		UpdateVisibility(0);
		
		_initializing = false;
	}

	private void IconSelected(long index)
	{
		_selectedIcon = _iconList.GetItemText((int)index);
		_iconList.Text = _selectedIcon;
		RaiseFieldChanged();
	}

	private string _selectedIcon = "Circle";

	private IconLibrary _icons;

	public void SetIcons(IconLibrary icons)
	{
		_icons = icons;
		_icons.LoadOptionButton(_iconList);
	}
	
	private void TypeChanged(long index)
	{
		UpdateVisibility(index);
		RaiseFieldChanged();
	}

	private void UpdateVisibility(long index)
	{
		_text.Visible = (index == 0);
		_qtyPicker.Visible = (index > 0);
		_iconList.Visible = (index == 1);
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
				qt.Caption = _selectedIcon;
				break;



		}

		qt.Quantity = _qtyPicker.Selected + 1;

		return qt;
	}

	public void SetQuickTextureField(QuickTextureField field)
	{
		if (field == null) return;

		_initializing = true;

		_colorPicker.Color = field.ForegroundColor;

		switch (field.FaceType)
		{
			case TextureFactory.TextureObjectType.Text:
				_optionTypes.Select(0);
				_text.Text = field.Caption ?? "";
				break;

			case TextureFactory.TextureObjectType.CoreShape:
				_optionTypes.Select(1);
				_selectedIcon = field.Caption ?? "Circle";
				if (_iconList != null)
				{
					for (int i = 0; i < _iconList.ItemCount; i++)
					{
						if (_iconList.GetItemText(i) == _selectedIcon)
						{
							_iconList.Select(i);
							break;
						}
					}
				}
				break;
		}

		if (_qtyPicker != null && field.Quantity > 0)
		{
			_qtyPicker.Select(field.Quantity - 1);
		}

		UpdateVisibility(_optionTypes.Selected);

		_initializing = false;
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

	public int Quantity { get; set; } = 1;
}

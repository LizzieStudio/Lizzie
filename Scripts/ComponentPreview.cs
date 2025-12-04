using Godot;
using System;
using System.Collections.Generic;

public partial class ComponentPreview : Panel
{
	private Node3D _parentNode;
	private Label _previewLabel;
	private HBoxContainer _multiPreview;
	private Button _firstButton;
	private Button _prevButton;
	private Button _nextButton;
	private Button _lastButton;
	private Label _curItemLabel;

	private Button _spinButton;
	private Button _frontView;
	private Button _backView;
	
	public override void _Ready()
	{
		_parentNode = GetNode<Node3D>("SubViewportContainer/SubViewport/Node3D");
		_previewLabel = GetNode<Label>("PreviewLabel");
		_multiPreview = GetNode<HBoxContainer>("Multipreview");

		_firstButton = GetNode<Button>("%FirstItem");
		_firstButton.Pressed += () => SetPreviewItem(0);
		
		_prevButton = GetNode<Button>("%PrevItem");
		_prevButton.Pressed += () => ChangePreviewItem(-1);
		
		_nextButton = GetNode<Button>("%NextItem");
		_nextButton.Pressed += () => ChangePreviewItem(1);
		
		_lastButton = GetNode<Button>("%LastItem");
		_lastButton.Pressed += () => SetPreviewItem(ItemCount-1);

		_curItemLabel = GetNode<Label>("%CurItemLabel");

		_spinButton = GetNode<Button>("%SpinButton");
		
		_frontView = GetNode<Button>("%FrontView");
		_frontView.Pressed += () => ShowView(0);
		
		_backView = GetNode<Button>("%BackView");
		_backView.Pressed += () => ShowView(180);
		
		UpdateMultiLabel();

	}

	public override void _Process(double delta)
	{
		if (_component != null && _spinButton.ButtonPressed)
		{
			_component.Rotation += new Vector3(0,(float)delta, 0);
		}
	}

	private VisualComponentBase _component;

	private bool _componentActive;
	
	public void SetComponent(VisualComponentBase component, Vector3 rotation)
	{
		if (_componentActive)
		{
			ClearComponent();
		}

		_component = component;
		_componentActive = true;
		_component.Rotation = rotation;
		_parentNode.AddChild(_component);
	}

	public void ClearComponent()
	{
		if (_component == null) return;
		_component.QueueFree();
		_component = null;
		_componentActive = false;
	}

	public void SetComponentVisibility(bool visibility)
	{
		if (_component == null) return;
		_component.Visible = visibility;
	}

	public void Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
	{
		if (_component != null)
		{
			_component.Build(parameters, textureFactory);
		}
	}
	
	#region Multi-preview mode

	private bool _multiItemMode;

	public bool MultiItemMode
	{
		get => _multiItemMode;
		set
		{
			_multiItemMode = value;
			_previewLabel.Visible = !value;
			_multiPreview.Visible = value;
		}
	}

	private void ChangePreviewItem(int delta)
	{
		CurrentItem += delta;
		CurrentItem = Math.Clamp(CurrentItem, 0, ItemCount - 1);
		UpdateMultiLabel();
		ItemSelected?.Invoke(this, new ItemSelectedEventArgs(ItemCount) );
	}

	private void UpdateMultiLabel()
	{
		_curItemLabel.Text = $"{CurrentItem + 1} of {ItemCount}";
	}

	private void SetPreviewItem(int item)
	{
		CurrentItem = Math.Clamp(item, 0, ItemCount - 1);
		UpdateMultiLabel();
		ItemSelected?.Invoke(this, new ItemSelectedEventArgs(ItemCount) );
	}

	private int _itemCount = 1;

	public int ItemCount
	{
		get => _itemCount;
		set
		{
			if (value < 1)
			{
				_itemCount = 1;
			}
			else
			{
				_itemCount = value;
			}
			UpdateMultiLabel();
		}
	} 
	
	public int CurrentItem { get; set; }
	
	public event EventHandler<ItemSelectedEventArgs> ItemSelected;
	
	#endregion

	#region Viewing controls

	private void ShowView(float angle)
	{
		_spinButton.ButtonPressed = false;	//stop spinning

		var r = new Vector3(_component.Rotation.X, Mathf.DegToRad(angle), _component.Rotation.Z);
		_component.Rotation = r;
	}
	
	
	#endregion
	
}

public class ItemSelectedEventArgs : EventArgs
{
	public ItemSelectedEventArgs(int index)
	{
		Index = index;
	}
	public int Index { get; set; }
}


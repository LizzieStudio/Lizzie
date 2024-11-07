using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ComponentDefinition : HBoxContainer
{
	// Called when the node enters the scene tree for the first time.
	[Export] private ComponentTemplate[] _components;

	private VBoxContainer buttonPanel;

	private Panel _componentPanel;

	private string _buttonTemplate = "res://Scenes/ComponentPanels/component_type_button.tscn";	//button to copy for 'sidepane' buttons.

	private Dictionary<string, CanvasItem> _panelDictionary = new();

	private Button _createButton;

	private Button _cancelButton;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		buttonPanel = GetNode<VBoxContainer>("ButtonStrip");
		_componentPanel = GetNode<Panel>("ComponentPanel");
		
		var bg = new ButtonGroup();

		bool firstButton = true;
		
		foreach (var c in _components)
		{
			var b = CreateButton(c.ComponentName, c.Icon, bg);
			buttonPanel.AddChild(b);

			var ci = CreateComponentPanel(c.DefinitionDialogName);
			_componentPanel.AddChild(ci);

			_panelDictionary.Add(c.ComponentName, ci);

			if (firstButton)
			{
				b.ButtonPressed = true;
				CurName = c.ComponentName;
				firstButton = false;
			}
		}

		_createButton = GetNode<Button>("%CreateButton");
		_createButton.Pressed += CreateClicked;

		_cancelButton = GetNode<Button>("%CancelButton");
		_cancelButton.Pressed += CancelClicked;
	}
	

	private string _curItem;

	private void CreateClicked()
	{
		if (_panelDictionary[CurName] is ComponentPanelDialogResult r)
		{
			CreateObjectEventArgs e = new()
			{
				ComponentType = NameToType(CurName),
				Params = r.GetParams(),
			};

			var cd = _components.First(x => x.ComponentName == CurName);

			if (cd.PrototypeNames != null && cd.PrototypeNames.Length > 0 && r.PrototypeIndex < cd.PrototypeNames.Length)
			{
				e.PrototypeName = cd.PrototypeNames[r.PrototypeIndex];
			}
			else
			{
				e.PrototypeName = cd.PrototypeName;
			}
					
			CreateObject?.Invoke(this, e);
		}
		else
		{
			GD.PrintErr($"{_panelDictionary[CurName].Name} is NOT CPDR");
		}
	}

	private void CancelClicked()
	{
		CancelDialog?.Invoke(this, EventArgs.Empty);
	}
	
	public string CurName
	{
		get => _curItem;
		set
		{
			_curItem = value;
			UpdatePanelVisibility(_curItem);
		}
	}

	private void UpdatePanelVisibility(string name)
	{
		foreach (var kv in _panelDictionary)
		{
			kv.Value.Visible = (kv.Key == name);
		}
	}

	private Button CreateButton(string name, Texture2D icon, ButtonGroup bg)
	{
		var scene = ResourceLoader.Load<PackedScene>(_buttonTemplate).Instantiate();
		
		if (scene is Button b)
		{
			b.Text = name;
			b.Icon = icon;
			b.ButtonGroup = bg;

			b.Pressed += () => ButtonPressed(name);
			return b;
		}

		return new Button();
		
	}

	public void ButtonPressed(string name)
	{
		CurName = name;
	}


	private CanvasItem CreateComponentPanel(string _panelTemplate)
	{
		var scene = ResourceLoader.Load<PackedScene>(_panelTemplate).Instantiate();
		
		if (scene is CanvasItem ci)
		{
			ci.Visible = false;	 //all panels start out hidden
			return ci;
		}
		
		GD.Print("Not Canvas Item");

		return null;
	}
	
	public event EventHandler<CreateObjectEventArgs> CreateObject;
	public event EventHandler<EventArgs> CancelDialog; 

	public VisualComponentBase.VisualComponentType NameToType(string name)
	{
		return _components.First(x => x.ComponentName == name).ComponentType;
	}

	public string TypeToName(VisualComponentBase.VisualComponentType componentType)
	{
		return _components.First(x => x.ComponentType == componentType).ComponentName;
	}
}

public class CreateObjectEventArgs: EventArgs
{
	public Dictionary<string, object> Params { get; set; }
	public VisualComponentBase.VisualComponentType ComponentType { get; set; }
	
	public string PrototypeName { get; set; }
}

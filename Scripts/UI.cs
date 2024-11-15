using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

public partial class UI : CanvasLayer
{
	[Export] private Color highlightFontColor;

	[Export] private Color baseFontColor;

	private HBoxContainer modeButtons;

	public event EventHandler<MasterModeChangeArgs> MasterModeChange;

	private ComponentDefinition _componentDefinition;

	private PopupMenu _insertMenu;
	private PopupMenu _helpMenu;

	private PopupMenu _componentPopup;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		modeButtons = GetNode<HBoxContainer>("Mode");
		var buttons = modeButtons.GetChildren();
		baseFontColor = new Color(1, 1, 1, 1);

		SetMasterMode(MasterMode.TwoD);

		_componentDefinition = GetNode<ComponentDefinition>("ComponentDefinition");
		_componentDefinition.CreateObject += OnCreateObject;
		_componentDefinition.CancelDialog += OnCancelCreate;

		_insertMenu = GetNode<PopupMenu>("MenuBar/Insert");
		_insertMenu.AddItem("Component", 1);
		_insertMenu.AddItem("Zone", 2);
		_insertMenu.IdPressed += OnInsertMenuSelection;

		_helpMenu = GetNode<PopupMenu>("MenuBar/Help");
		_helpMenu.AddItem("Test Function", 1);
		_helpMenu.IdPressed += OnHelpMenuSelection;

		_componentPopup = GetNode<PopupMenu>("ComponentPopup");
		_componentPopup.IdPressed += PopupMenuCommandSelected;
		_componentPopup.CloseRequested += ComponentPopupClosed;
	}

	public override void _Process(double delta)
	{
		//Below is a hack to work around the CloseRequested signal not getting fired properly

		if (_popupShown && _componentPopup.Visible)
		{
			_popupShown = false;
			ComponentPopupClosed();
		}
	}

	private void ComponentPopupClosed()
	{
		GetParent<GameController>().ComponentPopupClosed();
	}


	private void AddItemToPopupMenu(PopupMenu popup, VisualCommand command, string caption, string icon,
		bool enabled = true, bool checkable = false, bool isChecked = false)
	{
		int index = -1;
		int id = (int)command;

		if (checkable)
		{
			if (!string.IsNullOrEmpty(icon))
			{
				popup.AddCheckItem(caption, id);
			}
			else
			{
				//TODO Enable icon
				popup.AddCheckItem(caption, id);
			}

			index = popup.GetItemIndex(id);
			popup.SetItemChecked(index, isChecked);
		}
		else
		{
			if (!string.IsNullOrEmpty(icon))
			{
				popup.AddItem(caption, id);
			}
			else
			{
				//TODO Enable icon
				popup.AddItem(caption, id);
			}

			index = popup.GetItemIndex(id);
		}

		popup.SetItemDisabled(index, !enabled);
	}

	//we need to save which components are being affected by the right-click menu when it pops up
	private List<VisualComponentBase> _popupComponents;

	public void BuildPopupMenu(List<VisualComponentBase> components)
	{
		if (components.Count == 0) return; //TODO Right click menu for table surface?

		_popupComponents = components;

		var comDic = new Dictionary<VisualCommand, int>();

		var fullCommands = new List<MenuCommand>();

		foreach (var c in components)
		{
			var cList = c.GetMenuCommands();
			foreach (var m in cList)
			{
				fullCommands.Add(m);
				if (comDic.ContainsKey(m.Command))
				{
					comDic[m.Command]++;
				}
				else
				{
					comDic.Add(m.Command, 1);
				}
			}
		}

		//only include menu commands that are valid for all selected items
		var commands = comDic.Where(x => x.Value == components.Count)
			.Select(y => y.Key);

		_componentPopup.Clear();

		if (commands.Any(x => x == VisualCommand.ToggleLock))
		{
			var isChecked = fullCommands.Where(x => x.Command == VisualCommand.ToggleLock)
				.All(y => y.IsChecked);
			AddItemToPopupMenu(_componentPopup, VisualCommand.ToggleLock, "Frozen", string.Empty, true,
				true, isChecked);
		}

		if (commands.Any(x => x == VisualCommand.Roll))
			AddItemToPopupMenu(_componentPopup, VisualCommand.Roll, "Roll", string.Empty);

		if (commands.Any(x => x == VisualCommand.Flip))
			AddItemToPopupMenu(_componentPopup, VisualCommand.Flip, "Flip", string.Empty);

		if (commands.Any(x => x == VisualCommand.Delete))
			AddItemToPopupMenu(_componentPopup, VisualCommand.Delete, "Delete", string.Empty);
		
		if (commands.Any(x => x == VisualCommand.Shuffle))
			AddItemToPopupMenu(_componentPopup, VisualCommand.Shuffle, "Shuffle", string.Empty);
	}

	private void PopupMenuCommandSelected(long id)
	{
		if (id >= (int)VisualCommand.MaximumVC) return;

		VisualCommand vc = (VisualCommand)id;
		if (GetParent() is GameController gc)
		{
			gc.ProcessPopupCommand(vc, _popupComponents);
		}
	}


	private void OnHelpMenuSelection(long id)
	{
		var p = GetParent<GameController>();
		p.TestFunction();
	}


	private void OnInsertMenuSelection(long id)
	{
		if (id == 1) _componentDefinition.Visible = true;
	}

	private void OnInsertPressed()
	{
		_componentDefinition.Visible = true;
	}

	public event EventHandler<CreateObjectEventArgs> CreateObject;

	private void OnCreateObject(object sender, CreateObjectEventArgs args)
	{
		_componentDefinition.Visible = false;
		CreateObject?.Invoke(this, args);
	}

	private void OnCancelCreate(object sender, EventArgs e)
	{
		_componentDefinition.Visible = false;
	}


	private bool _popupShown;

	public void ShowComponentPopup(Vector2I position)
	{
		_componentPopup.Visible = true;
		_componentPopup.Position = position;
		_popupShown = true;
	}

	public void HideComponentPopup()
	{
		_componentPopup.Visible = false;
	}

	public enum MasterMode
	{
		TwoD,
		ThreeD,
		Designer
	};

	public MasterMode CurMasterMode { get; set; }

	private void SetMasterMode(MasterMode mode)
	{
		GD.Print($"Set Master Mode {mode}");
		var buttons = modeButtons.GetChildren();
		foreach (var i in buttons)
		{
			if (i is Button b)
			{
				b.RemoveThemeColorOverride("font_color");
				b.RemoveThemeColorOverride("font_focus_color");
			}
		}

		var buttonNum = 0;

		switch (mode)
		{
			case MasterMode.TwoD:
				buttonNum = 0;
				break;
			case MasterMode.ThreeD:
				buttonNum = 1;
				break;
			case MasterMode.Designer:
				buttonNum = 2;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
		}

		if (buttons[buttonNum] is Button target)
		{
			target.AddThemeColorOverride("font_color", highlightFontColor);
			target.AddThemeColorOverride("font_focus_color", highlightFontColor);
		}

		CurMasterMode = mode;
		MasterModeChange?.Invoke(this, new MasterModeChangeArgs { NewMode = mode });
	}


	private void _on_play_2d_pressed()
	{
		SetMasterMode(MasterMode.TwoD);
	}


	private void _on_play_3d_pressed()
	{
		SetMasterMode(MasterMode.ThreeD);
	}


	private void _on_designer_pressed()
	{
		SetMasterMode(MasterMode.Designer);
		TextureTest();
	}

	private void TextureTest()
	{
		var sv = GetNode<SubViewport>("SubViewport");
		var target = GetNode<TextureRect>("TestRect");

		var t = sv.GetTexture();
		target.Texture = t;
	}


	public const int LongClickTime = 1000;
}

public class MasterModeChangeArgs : EventArgs
{
	public UI.MasterMode NewMode { get; set; }
}

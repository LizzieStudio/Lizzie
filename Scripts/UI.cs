using Godot;
using System;
using System.ComponentModel;

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
		_insertMenu.AddItem("Component",1);
		_insertMenu.AddItem("Zone", 2);
		_insertMenu.IdPressed += OnInsertMenuSelection;
		
		_helpMenu = GetNode<PopupMenu>("MenuBar/Help");
		_helpMenu.AddItem("Test Function",1);
		_helpMenu.IdPressed += OnHelpMenuSelection;

		_componentPopup = GetNode<PopupMenu>("ComponentPopup");
		_componentPopup.IdPressed += PopupMenuCommandSelected;
	}
	
	

	private const int PopupLockId = 0;
	private const int PopupFlipId = 1;
	private const int PopupRotateCwId = 2;
	private const int PopupRotateCcwId = 3;
	private const int PopupDeleteId = 4;
	
	private void PopupMenuCommandSelected(long id)
	{
		GD.Print($"Manu {id}");
		if (id == PopupFlipId)
		{
			if (GetParent() is GameController gc)
			{
				GD.Print("Flipping");
				gc.ProcessCommand(SceneController.VisualCommand.Flip);
			}
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

	// Called every frame. 'delta' is the elapsed time since the previous frame.

	public void ShowComponentPopup(Vector2I position)
	{
		_componentPopup.Visible = true;
		_componentPopup.Position = position;
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
			target.AddThemeColorOverride("font_color",highlightFontColor);
			target.AddThemeColorOverride("font_focus_color",highlightFontColor);
		}
		
		CurMasterMode = mode;
		MasterModeChange?.Invoke(this, new MasterModeChangeArgs{NewMode =mode});
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





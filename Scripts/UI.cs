using Godot;
using System;

public partial class UI : CanvasLayer
{
	[Export] private Color highlightFontColor;

	[Export] private Color baseFontColor;

	private HBoxContainer modeButtons;

	public event EventHandler<MasterModeChangeArgs> MasterModeChange;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		modeButtons = GetNode<HBoxContainer>("Mode");
		var buttons = modeButtons.GetChildren();
		baseFontColor = new Color(1, 1, 1, 1);
		
		
		//test
		if (buttons[1] is Button b)
		{
			b.AddThemeColorOverride("font_color",highlightFontColor);
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
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
	}
	
}

public class MasterModeChangeArgs : EventArgs
{
	public UI.MasterMode NewMode { get; set; }
}





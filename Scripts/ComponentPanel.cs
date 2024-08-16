using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class ComponentPanel : Panel
{
	[Export] private ComponentTemplate[] components;

	private Button _create;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{

	}

	private void CreatePressed()
	{
		foreach (var o in GetChildren())
		{
			
		}

	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}

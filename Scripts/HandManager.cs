using Godot;
using System;

public partial class HandManager : Panel
{
	private float _openPosition;

	private float _closedPosition;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_openPosition = Position.Y;
		_closedPosition = _openPosition + 180;
		Position = new Vector2(0, _closedPosition);
		MouseEntered += ShowPanel;
		MouseExited += HidePanel;
	}

	private void HidePanel()
	{
		_targetPos = _closedPosition;
		_panelMoveDir = 1;
	}

	private void ShowPanel()
	{
		_targetPos = _openPosition;
		_panelMoveDir = -1;
	}

	[Export] private float _openSpeed = 1000;
	private int _panelMoveDir;
	private float _targetPos;
	
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_panelMoveDir != 0)
		{
			var curPos = Position.Y;
			
			float newPos = curPos +  _panelMoveDir * ((float)delta * _openSpeed);

			if (_panelMoveDir > 0)
			{
				newPos = Math.Min(newPos, _targetPos);
			}
			else
			{
				newPos = Math.Max(newPos, _targetPos);
			}

			Position = new Vector2(0, newPos);
			if (newPos == _targetPos) _panelMoveDir = 0;
		}
	}
}

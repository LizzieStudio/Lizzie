using Godot;
using System;

public partial class PlayerHandsPanel : Panel
{
	private Button _showHideButton;
	private bool _isHidden = false;

	/// <summary>
	/// Fired when the show/hide button is pressed.
	/// The bool argument is <c>true</c> when the panel is now hidden, <c>false</c> when shown.
	/// </summary>
	public event EventHandler<bool> ShowHideToggled;

	public override void _Ready()
	{
		_showHideButton = GetNode<Button>("%ShowHideButton");
		_showHideButton.Pressed += OnShowHideButtonPressed;
	}

	private void OnShowHideButtonPressed()
	{
		_isHidden = !_isHidden;
		_showHideButton.Text = _isHidden ? "<" : ">";
		ShowHideToggled?.Invoke(this, _isHidden);
	}

	public override void _Process(double delta)
	{
	}
}


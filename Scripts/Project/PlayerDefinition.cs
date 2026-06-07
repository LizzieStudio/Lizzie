using Godot;
using System;

public partial class PlayerDefinition : HBoxContainer
{
    private Label _playerNumber;
    private LineEdit _playerName;
	private ColorPickerButton _playerColor;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_playerNumber = GetNode<Label>("%PlayerNumber");
		_playerName = GetNode<LineEdit>("%PlayerName");
		_playerColor = GetNode<ColorPickerButton>("%PlayerColor");

        _playerNumber.Text = _pNumber;
        _playerName.Text = _pName;
        _playerColor.Color = _pColor;
}

    private string _pNumber;
    private string _pName;
    private Color _pColor;
	

    public void SetPlayerInfo(int playerNumber, ProjectPlayerSettings settings)
    {
        _pNumber = $"Player {playerNumber}";
        _pName = settings.Name;
        _pColor = new Color(settings.ColorR, settings.ColorG, settings.ColorB, settings.ColorA);

        if (IsNodeReady())
        {
            _playerNumber.Text = _pNumber;
            _playerName.Text = _pName;
            _playerColor.Color = _pColor;
        }
    }
	
	public (string, Color) GetPlayerInfo()
    {
        return (_playerName.Text, _playerColor.Color);
    }
}
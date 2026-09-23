using System;
using Godot;

public partial class PlayerDefinition : HBoxContainer
{
    private Label _playerNumber;
    private LineEdit _playerName;
    private ColorPickerButton _playerColor;
    private CheckBox _playerAdmin;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _playerNumber = GetNode<Label>("%PlayerNumber");
        _playerName = GetNode<LineEdit>("%PlayerName");
        _playerColor = GetNode<ColorPickerButton>("%PlayerColor");
        _playerAdmin = GetNode<CheckBox>("%PlayerAdmin");
    }

    public void SetPlayerInfo(int playerNumber, ProjectPlayerSettings settings)
    {
        _playerNumber.Text = $"Player {playerNumber}";
        _playerName.Text = settings.Name;
        _playerColor.Color = settings.Color;
        _playerAdmin.ButtonPressed = settings.IsAdmin;
    }

    public (string, Color, bool) GetPlayerInfo()
    {
        return (_playerName.Text, _playerColor.Color, _playerAdmin.ButtonPressed);
    }
}

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

        _playerNumber.Text = _pNumber;
        _playerName.Text = _pName;
        _playerColor.Color = _pColor;
        _playerAdmin.ButtonPressed = _pAdmin;
    }

    private string _pNumber;
    private string _pName;
    private Color _pColor;
    private bool _pAdmin;

    public void SetPlayerInfo(int playerNumber, ProjectPlayerSettings settings)
    {
        _pNumber = $"Player {playerNumber}";
        _pName = settings.Name;
        _pColor = new Color(settings.ColorR, settings.ColorG, settings.ColorB, settings.ColorA);
        _pAdmin = settings.IsAdmin;

        if (IsNodeReady())
        {
            _playerNumber.Text = _pNumber;
            _playerName.Text = _pName;
            _playerColor.Color = _pColor;
            _playerAdmin.ButtonPressed = _pAdmin;
        }
    }

    public (string, Color, bool) GetPlayerInfo()
    {
        return (_playerName.Text, _playerColor.Color, _playerAdmin.ButtonPressed);
    }
}

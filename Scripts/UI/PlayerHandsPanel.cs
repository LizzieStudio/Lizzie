using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class PlayerHandsPanel : Panel
{
    private Button _showHideButton;
    private bool _isHidden = false;

    private VBoxContainer _playerHandsContainer;

    public event EventHandler<bool> IsShowingChanged;

    public override void _Ready()
    {
        _showHideButton = GetNode<Button>("%ShowHideButton");
        _showHideButton.Pressed += OnShowHideButtonPressed;

        _playerHandsContainer = GetNode<VBoxContainer>("%PlayerHands");
    }

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
    }

    private void OnShowHideButtonPressed()
    {
        _isHidden = !_isHidden;
        _showHideButton.Text = _isHidden ? "<" : ">";
        IsShowingChanged?.Invoke(this, !_isHidden);
    }

    private readonly Dictionary<int, (VBoxContainer Row, Label Label)> _rows = new();

    private void Sync(IRecordReader R)
    {
        var players = R.Single<ProjectGameSettings>().Players;
        int localSeat = R.LocalSeat();

        // the local player is shown in HandManager, not here
        var seats = Enumerable.Range(0, players.Length).Where(s => s != localSeat).ToList();

        foreach (var seat in _rows.Keys.Except(seats).ToList())
        {
            _rows.Remove(seat, out var gone);
            _playerHandsContainer.RemoveChild(gone.Row);
            gone.Row.QueueFree();
        }

        for (int i = 0; i < seats.Count; i++)
        {
            int seat = seats[i];
            if (!_rows.TryGetValue(seat, out var row))
                _rows[seat] = row = AddRow(seat);
            _playerHandsContainer.MoveChild(row.Row, i);

            var player = players[seat];
            row.Label.Text = $"{player.Name}  ({PlayerHandService.GetHand(R, seat).Count})";
            row.Label.AddThemeColorOverride("font_color", player.Color);
        }
    }

    private (VBoxContainer Row, Label Label) AddRow(int seat)
    {
        var row = new VBoxContainer();
        _playerHandsContainer.AddChild(row);
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var label = new Label();
        row.AddChild(label);

        var hand = new HandRow { Back = true };
        row.AddChild(hand);
        hand.Seat = seat;
        hand.CustomMinimumSize = new Vector2(0, 50);
        hand.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        return (row, label);
    }

    public override void _Process(double delta) { }
}

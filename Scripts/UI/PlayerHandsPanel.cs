using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class PlayerHandsPanel : Panel
{
    private Button _showHideButton;
    private bool _isHidden = false;

    private VBoxContainer _playerHandsContainer;

    [Export]
    private PackedScene _opponentHandScene;

    public event EventHandler<bool> IsShowingChanged;

    public override void _Ready()
    {
        _showHideButton = GetNode<Button>("%ShowHideButton");
        _showHideButton.Pressed += OnShowHideButtonPressed;

        _playerHandsContainer = GetNode<VBoxContainer>("%PlayerHands");
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    private void OnShowHideButtonPressed()
    {
        _isHidden = !_isHidden;
        _showHideButton.Text = _isHidden ? "<" : ">";
        IsShowingChanged?.Invoke(this, !_isHidden);
    }

    private readonly Dictionary<Ref<Seat>, (VBoxContainer Row, Label Label)> _rows = new();

    private void Sync(IWatchReader R)
    {
        var seats = R.Seats();
        var stage = ProjectService.Instance.GameObjects.ShownStage(R);
        var localSeat = R.LocalSeat();

        // the local player is shown in HandManager, not here
        var others = seats.Where(s => s.Id != localSeat).ToList();

        foreach (var seat in _rows.Keys.Except(others.Select(s => s.Id)).ToList())
        {
            _rows.Remove(seat, out var gone);
            _playerHandsContainer.RemoveChild(gone.Row);
            gone.Row.QueueFree();
        }

        for (int i = 0; i < others.Count; i++)
        {
            var seat = others[i];
            if (!_rows.TryGetValue(seat.Id, out var row))
                _rows[seat.Id] = row = AddRow(seat.Id);
            _playerHandsContainer.MoveChild(row.Row, i);

            var label = seat.Label(seats.IndexOf(seat));
            row.Label.Text = $"{label}  ({PlayerHandService.GetHand(R, stage, seat.Id).Count})";
            row.Label.AddThemeColorOverride("font_color", seat.Color);
        }
    }

    private (VBoxContainer Row, Label Label) AddRow(Ref<Seat> seat)
    {
        var row = _opponentHandScene.Instantiate<VBoxContainer>();
        _playerHandsContainer.AddChild(row);
        row.GetNode<HandRow>("HandRow").Seat = seat;
        return (row, row.GetNode<Label>("Label"));
    }

    public override void _Process(double delta) { }
}

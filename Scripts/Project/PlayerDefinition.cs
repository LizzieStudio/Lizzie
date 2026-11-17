using Godot;

namespace Lizzie;

public partial class PlayerDefinition : HBoxContainer
{
    private Label _dragHandle;
    private LineEdit _playerName;
    private ColorPickerButton _playerColor;
    private CheckBox _playerAdmin;
    private Button _remove;
    public Ref<Seat> SeatRef { get; set; }

    public override void _Ready()
    {
        _dragHandle = GetNode<Label>("%DragHandle");
        _playerName = GetNode<LineEdit>("%PlayerName");
        _playerColor = GetNode<ColorPickerButton>("%PlayerColor");
        _playerAdmin = GetNode<CheckBox>("%PlayerAdmin");
        _remove = GetNode<Button>("%Remove");

        // This triggers on defocus and hitting Enter.
        _playerName.EditingToggled += editing =>
        {
            var name = _playerName.Text == "" ? null : _playerName.Text;
            if (!editing && Atoms.R.Get(Seat.ById, SeatRef)?.Name != name)
                Atoms.R.Update(this, Seat.ById, SeatRef, s => s with { Name = name });
        };
        // When they hit Escape, put the name back.
        _playerName.GuiInput += e =>
        {
            if (e.IsActionPressed("ui_cancel"))
                _playerName.Text = Atoms.R.Get(Seat.ById, SeatRef)?.Name;
        };
        _playerColor.PopupClosed += () =>
            Atoms.R.Update(this, Seat.ById, SeatRef, s => s with { Color = _playerColor.Color });
        _playerAdmin.Toggled += on =>
            Atoms.R.Update(this, Seat.ById, SeatRef, s => s with { IsAdmin = on });
        _remove.Pressed += () =>
            Atoms.R.Update(this, Seat.ById, SeatRef, s => s with { Deleted = true });

        _dragHandle.SetDragForwarding(
            Callable.From<Vector2, SeatDrag>(_ => new SeatDrag { Seat = SeatRef }),
            default,
            default
        );
    }

    public void ShowSeat(Seat seat, int place)
    {
        SeatRef = seat.Id;
        _playerName.PlaceholderText = $"Seat {place + 1}";
        if (!_playerName.IsEditing() && _playerName.Text != (seat.Name ?? ""))
            _playerName.Text = seat.Name;
        if (!_playerColor.GetPopup().Visible)
            _playerColor.Color = seat.Color;
        _playerAdmin.SetPressedNoSignal(seat.IsAdmin);
    }
}

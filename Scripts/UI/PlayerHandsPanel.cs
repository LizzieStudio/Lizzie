using System;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class PlayerHandsPanel : PanelContainer
{
    private Button _showHideButton;
    private bool _isHidden = false;

    private VBoxContainer _playerHandsContainer;

    [Export]
    private PackedScene _opponentHandScene;

    [Export]
    private Control _localPlayerHand;

    /// <summary>
    /// The space left between this panel and the hand panel.
    /// </summary>
    [Export(PropertyHint.Range, "0,100,1,suffix:px")]
    public float HandGap { get; set; } = 6;

    private ScrollContainer _scroll;

    public event EventHandler<bool> IsShowingChanged;

    public override void _Ready()
    {
        _showHideButton = GetNode<Button>("%ShowHideButton");
        _showHideButton.Pressed += OnShowHideButtonPressed;

        _playerHandsContainer = GetNode<VBoxContainer>("%PlayerHands");
        _rows = new(_playerHandsContainer, AddRow);
        _scroll = GetNode<ScrollContainer>("%Scroll");
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

    private ChildList<Ref<Seat>, VBoxContainer> _rows;

    private void Sync(IWatchReader R)
    {
        var seats = R.Get(Seat.Live);
        var stage = ProjectService.Instance.GameObjects.ShownStage(R);
        var localSeat = R.LocalSeat();

        // the local player is shown in HandManager, not here
        var others = seats.Where(s => s.Id != localSeat).ToList();
        var rows = _rows.Show(others, s => s.Id);
        for (int i = 0; i < others.Count; i++)
        {
            var seat = others[i];
            var label = rows[i].GetNode<Label>("Label");
            int cards = R.Count(ComponentPosition.ByContainer, new(stage, new InHand(seat.Id)));
            label.Text = $"{seat.Label(seats.IndexOf(seat))}  ({cards})";
            label.AddThemeColorOverride("font_color", seat.Color);
        }
    }

    private VBoxContainer AddRow(Ref<Seat> seat)
    {
        var row = _opponentHandScene.Instantiate<VBoxContainer>();
        _playerHandsContainer.AddChild(row);
        row.GetNode<HandRow>("HandRow").Seat = seat;
        return row;
    }

    public override void _Process(double delta)
    {
        // The panel switches to scrolling when it doesn't have enough room.
        // There's no Godot layout for this. So we do it ourselves.
        float rest = Size.Y - _scroll.Size.Y;
        float room = _localPlayerHand.GlobalPosition.Y - HandGap - GlobalPosition.Y - rest;
        var max = new Vector2(-1, Math.Max(room, 0));
        if (_scroll.CustomMaximumSize == max)
            return;
        _scroll.CustomMaximumSize = max;
        // The scroll's minimum size is set by the maximum, but Godot doesn't update it automatically.
        _scroll.UpdateMinimumSize();
    }
}

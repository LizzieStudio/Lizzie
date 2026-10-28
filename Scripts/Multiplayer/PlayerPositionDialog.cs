using System.Collections.Generic;
using Godot;

namespace Lizzie;

/// <summary>
/// Modal dialog that lets the local player pick a seat, or none.
/// Instantiate, add to the scene tree, then call Show().
/// The dialog frees itself when the player confirms or cancels.
/// </summary>
public partial class PlayerPositionDialog : ConfirmationDialog
{
    private ItemList _seatList;
    private Label _statusLabel;

    // Maps list index -> seat, or Empty for no seat
    private readonly List<Ref<Seat>> _seatIndexMap = new();

    public override void _Ready()
    {
        Title = "Choose Your Player Position";
        OkButtonText = "Confirm";
        CancelButtonText = "Cancel";
        MinSize = new Vector2I(360, 300);
        Exclusive = true;

        _seatList = GetNode<ItemList>("%SeatList");
        _seatList.CustomMinimumSize = new Vector2(340, 180);
        _seatList.SelectMode = ItemList.SelectModeEnum.Single;

        _statusLabel = GetNode<Label>("%StatusLabel");
        _statusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        _statusLabel.Visible = false;

        Confirmed += OnConfirmed;
        Canceled += OnCanceled;

        EventBus.Instance?.Subscribe<RequestPlayerPositionEvent>(OnReprompt);
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RequestPlayerPositionEvent>(OnReprompt);
    }

    // -------------------------------------------------------------------------
    // Public helper to show the dialog positioned in the center of its parent
    // -------------------------------------------------------------------------
    public void ShowCentered()
    {
        PopupCentered();
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private void Sync(IWatchReader R)
    {
        var selected = _seatList.GetSelectedItems();
        var selectedSeat = selected.Length > 0 ? _seatIndexMap[selected[0]] : R.LocalSeat();
        // A player without a seat starts on the first free one, unless they picked No Seat.
        bool keep = selected.Length > 0 || !selectedSeat.IsEmpty;

        _seatList.Clear();
        _seatIndexMap.Clear();

        var seats = R.Get(Seat.Live);
        for (int i = 0; i < seats.Count; i++)
        {
            var seat = seats[i];
            bool current = R.LocalSeat() == seat.Id;
            bool available = current || !R.IsSeatTaken(seat.Id);

            var label = seat.Label(i);
            if (current)
                label += "  [current]";
            else if (!available)
                label += "  [taken]";

            _seatList.AddItem(label);
            _seatList.SetItemDisabled(_seatList.ItemCount - 1, !available);

            // Color the icon using the seat's color
            _seatList.SetItemCustomFgColor(_seatList.ItemCount - 1, seat.Color);

            _seatIndexMap.Add(seat.Id);
        }

        _seatList.AddItem("No Seat");
        _seatIndexMap.Add(Ref<Seat>.Empty);

        // Keep the player's choice selected while it's still available
        var kept = keep ? _seatIndexMap.IndexOf(selectedSeat) : -1;
        if (kept >= 0 && !_seatList.IsItemDisabled(kept))
        {
            _seatList.Select(kept);
            return;
        }

        // Otherwise pre-select the first available entry
        for (int i = 0; i < _seatList.ItemCount; i++)
        {
            if (!_seatList.IsItemDisabled(i))
            {
                _seatList.Select(i);
                break;
            }
        }
    }

    private void OnConfirmed()
    {
        var selected = _seatList.GetSelectedItems();
        if (selected.Length == 0)
        {
            ShowStatus("Please select a position first.");
            // Re-open so the player can choose
            Callable.From(() => PopupCentered()).CallDeferred();
            return;
        }

        PresenceSynchronizer.Instance?.RequestSeat(_seatIndexMap[selected[0]]);
        // The dialog will be freed once the claim result comes back (or immediately for local).
        QueueFree();
    }

    private void OnCanceled()
    {
        QueueFree();
    }

    private void OnReprompt(RequestPlayerPositionEvent _)
    {
        // Another player claimed a seat; refresh availability.
        Atoms.R.QueueSync(this);
    }

    private void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
    }
}

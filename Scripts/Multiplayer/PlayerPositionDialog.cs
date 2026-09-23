using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Modal dialog that lets the local player pick a player position (or observer).
/// Instantiate, add to the scene tree, then call Show().
/// The dialog frees itself when the player confirms or cancels.
/// </summary>
public partial class PlayerPositionDialog : ConfirmationDialog
{
    private ItemList _seatList;
    private Label _statusLabel;

    // Maps list index -> seatIndex (-1 = observer)
    private readonly List<int> _seatIndexMap = new();

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

        if (PresenceSynchronizer.Instance != null)
            PresenceSynchronizer.Instance.SeatsChanged += OnSeatsChanged;
        EventBus.Instance?.Subscribe<RequestPlayerPositionEvent>(OnReprompt);

        PopulateList();
    }

    public override void _ExitTree()
    {
        if (PresenceSynchronizer.Instance != null)
            PresenceSynchronizer.Instance.SeatsChanged -= OnSeatsChanged;
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

    private void PopulateList()
    {
        _seatList.Clear();
        _seatIndexMap.Clear();

        var settings = ProjectService.Instance.Settings.Value;
        if (settings == null)
            return;

        for (int i = 0; i < settings.Players.Length; i++)
        {
            var player = settings.Players[i];
            bool available = PresenceSynchronizer.Instance?.IsAvailable(i) ?? true;

            var label = $"Player {i + 1}: {player.Name}";
            if (!available)
                label += "  [taken]";

            _seatList.AddItem(label);
            _seatList.SetItemDisabled(_seatList.ItemCount - 1, !available);

            // Colour the icon using the player's configured colour
            _seatList.SetItemCustomFgColor(_seatList.ItemCount - 1, player.Color);

            _seatIndexMap.Add(i);
        }

        if (settings.AllowObservers)
        {
            _seatList.AddItem("Observer  (watch only)");
            _seatIndexMap.Add(-1);
        }

        // Pre-select first available entry
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
            CallDeferred(nameof(PopupCentered));
            return;
        }

        int listIdx = selected[0];
        int seatIdx = _seatIndexMap[listIdx];

        PresenceSynchronizer.Instance?.RequestSeat(seatIdx);
        // The dialog will be freed once the claim result comes back (or immediately for local).
        QueueFree();
    }

    private void OnCanceled()
    {
        QueueFree();
    }

    private void OnSeatsChanged() => PopulateList();

    private void OnReprompt(RequestPlayerPositionEvent _)
    {
        // Another player claimed a seat; refresh availability.
        PopulateList();
    }

    private void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
    }
}

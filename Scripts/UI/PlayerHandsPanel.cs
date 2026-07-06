using System;
using System.Linq;
using Godot;

public partial class PlayerHandsPanel : Panel
{
    private Button _showHideButton;
    private bool _isHidden = false;

    private VBoxContainer _playerHandsContainer;

    /// <summary>
    /// Fired when the show/hide button is pressed.
    /// The bool argument is <c>true</c> when the panel is now hidden, <c>false</c> when shown.
    /// </summary>
    public event EventHandler<bool> ShowHideToggled;

    public override void _Ready()
    {
        _showHideButton = GetNode<Button>("%ShowHideButton");
        _showHideButton.Pressed += OnShowHideButtonPressed;

        _playerHandsContainer = GetNode<VBoxContainer>("%PlayerHands");

        EventBus.Instance.Subscribe<HandChangedEvent>(OnHandChanged);
        EventBus.Instance.Subscribe<PlayerSeatClaimedEvent>(OnSeatClaimed);
        EventBus.Instance.Subscribe<ProjectChangedEvent>(OnProjectChanged);

        RebuildOpponentHands();
    }

    public override void _ExitTree()
    {
        EventBus.Instance.Unsubscribe<HandChangedEvent>(OnHandChanged);
        EventBus.Instance.Unsubscribe<PlayerSeatClaimedEvent>(OnSeatClaimed);
        EventBus.Instance.Unsubscribe<ProjectChangedEvent>(OnProjectChanged);
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void OnHandChanged(HandChangedEvent evt)
    {
        // Only need to update if it is an opponent's seat
        if (evt.SeatIndex != PlayerHandService.LocalSeatIndex())
            RebuildOpponentHands();
    }

    private void OnSeatClaimed(PlayerSeatClaimedEvent _) => RebuildOpponentHands();

    private void OnProjectChanged(ProjectChangedEvent _) => RebuildOpponentHands();

    // -------------------------------------------------------------------------
    // Show/hide toggle
    // -------------------------------------------------------------------------

    private void OnShowHideButtonPressed()
    {
        _isHidden = !_isHidden;
        _showHideButton.Text = _isHidden ? "<" : ">";
        ShowHideToggled?.Invoke(this, _isHidden);
    }

    // -------------------------------------------------------------------------
    // Dynamic opponent-hand rows
    // -------------------------------------------------------------------------

    private void RebuildOpponentHands()
    {
        if (_playerHandsContainer == null)
            return;

        // Remove existing rows
        foreach (var child in _playerHandsContainer.GetChildren())
        {
            _playerHandsContainer.RemoveChild(child);
            child.QueueFree();
        }

        var settings = ProjectService.Instance.CurrentProject?.GameSettings;
        if (settings == null)
            return;

        int localSeat = PlayerHandService.LocalSeatIndex();

        for (int seatIndex = 0; seatIndex < settings.Players.Count; seatIndex++)
        {
            if (seatIndex == localSeat)
                continue; // local player is shown in HandManager, not here

            var playerSettings = settings.Players[seatIndex];
            var hand =
                PlayerHandService.Instance?.GetHand(seatIndex) ?? System.Array.Empty<VcToken>();

            // Row container for this opponent
            var row = new VBoxContainer();
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // Player name label  e.g. "Player 2 (3 cards)"
            var label = new Label();
            label.Text = $"{playerSettings.Name}  ({hand.Count})";
            label.AddThemeColorOverride(
                "font_color",
                new Color(
                    playerSettings.ColorR,
                    playerSettings.ColorG,
                    playerSettings.ColorB,
                    playerSettings.ColorA
                )
            );
            row.AddChild(label);

            // HBoxContainer holding card backs
            var hbox = new HBoxContainer();
            hbox.CustomMinimumSize = new Vector2(0, 50);
            hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            foreach (var card in hand)
            {
                var tex = new TextureRect();
                tex.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
                tex.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
                tex.Texture = GetCardBackTexture(card);
                tex.CustomMinimumSize = new Vector2(35, 50);
                hbox.AddChild(tex);
            }

            row.AddChild(hbox);
            _playerHandsContainer.AddChild(row);
        }
    }

    private static ImageTexture GetCardBackTexture(VcToken card)
    {
        if (card.BackTexture == null)
            return null;

        var fullImage = card.BackSprite;
        if (fullImage == null)
            return null;

        if (fullImage.IsCompressed())
            fullImage.Decompress();

        int hframes = Math.Max(1, card.BackHframes);
        int vframes = Math.Max(1, card.BackVframes);
        int frameW = fullImage.GetWidth() / hframes;
        int frameH = fullImage.GetHeight() / vframes;
        int col = card.BackFrame % hframes;
        int row = card.BackFrame / hframes;
        var region = new Rect2I(col * frameW, row * frameH, frameW, frameH);
        var frameImage = fullImage.GetRegion(region);
        return ImageTexture.CreateFromImage(frameImage);
    }

    public override void _Process(double delta) { }
}

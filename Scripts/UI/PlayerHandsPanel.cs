using System;
using System.Linq;
using Godot;

public partial class PlayerHandsPanel : Panel
{
    private Button _showHideButton;
    private bool _isHidden = false;

    private VBoxContainer _playerHandsContainer;

    private GameObjects _gameObjects;

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

        if (PresenceSynchronizer.Instance != null)
            PresenceSynchronizer.Instance.SeatsChanged += OnModelChanged;

        Callable.From(ConnectTable).CallDeferred();
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    public override void _ExitTree()
    {
        if (PresenceSynchronizer.Instance != null)
            PresenceSynchronizer.Instance.SeatsChanged -= OnModelChanged;
        if (_gameObjects != null && IsInstanceValid(_gameObjects))
            _gameObjects.TableChanged -= OnModelChanged;
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void ConnectTable()
    {
        _gameObjects = ProjectService.Instance?.GameObjects;
        if (_gameObjects != null)
            _gameObjects.TableChanged += OnModelChanged;
        OnModelChanged();
    }

    private void OnModelChanged() => ProjectService.Instance.QueueSync(this);

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

    private void Sync(IRecordReader R)
    {
        // Remove existing rows
        foreach (var child in _playerHandsContainer.GetChildren())
        {
            _playerHandsContainer.RemoveChild(child);
            child.QueueFree();
        }

        var settings = R.Value<ProjectGameSettings>();

        int localSeat = PlayerHandService.LocalSeatIndex();

        for (int seatIndex = 0; seatIndex < settings.Players.Length; seatIndex++)
        {
            if (seatIndex == localSeat)
                continue; // local player is shown in HandManager, not here

            var playerSettings = settings.Players[seatIndex];
            var hand = PlayerHandService.Instance?.GetHand(seatIndex) ?? Array.Empty<VcToken>();

            // Row container for this opponent
            var row = new VBoxContainer();
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // Player name label  e.g. "Player 2 (3 cards)"
            var label = new Label();
            label.Text = $"{playerSettings.Name}  ({hand.Count})";
            label.AddThemeColorOverride("font_color", playerSettings.Color);
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

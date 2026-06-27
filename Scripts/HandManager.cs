using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class HandManager : Panel
{
    private float _openPosition;
    private float _closedPosition;

    private const float ResizeHandleHeight = 8f;
    private bool _isResizing;
    private float _resizeDragStartY;
    private float _resizePanelStartY;
    private float _resizePanelStartHeight;

    private Button _openCloseButton;

    private List<VcToken> _cards = new();
    private HBoxContainer _handContainer;

    private Texture2D _openIcon;
    private Texture2D _closeIcon;

    // Drag-preview sprite shown while a 3D card is dragged over the hand panel
    private TextureRect _dragPreview;
    private VcToken _dragPreviewCard;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        UpdatePositions();
        Position = new Vector2(0, _openPosition);

        _openCloseButton = GetNode<Button>("HandLockButton");
        _openCloseButton.Pressed += TogglePanel;

        GetTree().Root.SizeChanged += OnWindowResized;

        _handContainer = GetNode<HBoxContainer>("%HandContainer");

        EventBus.Instance.Subscribe<AddToHandEvent>(OnAddToHand);
        EventBus.Instance.Subscribe<HandChangedEvent>(OnHandChanged);

        _openIcon = ResourceLoader.Load<Texture2D>(OpenIcon);
        _closeIcon = ResourceLoader.Load<Texture2D>(CloseIcon);

        _dragPreview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            Size = new Vector2(80, 120),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 10,
        };
        AddChild(_dragPreview);
    }

    private void OnAddToHand(AddToHandEvent obj)
    {
        // PlayerHandService handles storage; we just need to refresh our display
        // if the event targets the local seat.
        // (PlayerHandService publishes HandChangedEvent which triggers OnHandChanged.)
    }

    private void OnHandChanged(HandChangedEvent evt)
    {
        if (evt.SeatIndex == PlayerHandService.LocalSeatIndex())
            RefreshDisplay();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed && IsOverResizeHandle(mb.Position))
            {
                _isResizing = true;
                _resizeDragStartY = mb.GlobalPosition.Y;
                _resizePanelStartY = Position.Y;
                _resizePanelStartHeight = Size.Y;
                GetViewport().SetInputAsHandled();
                DisplayServer.CursorSetShape(DisplayServer.CursorShape.Vsize);
            }
            else if (!mb.Pressed && _isResizing)
            {
                _isResizing = false;
                UpdatePositions();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_isResizing)
            {
                float dy = motion.GlobalPosition.Y - _resizeDragStartY;
                float newY = _resizePanelStartY + dy;
                float newHeight = _resizePanelStartHeight - dy;
                float viewportHeight = GetViewport().GetVisibleRect().Size.Y;

                newHeight = Math.Max(newHeight, 50f);
                newY = Math.Min(newY, viewportHeight - 50f);

                Position = new Vector2(Position.X, newY);
                Size = new Vector2(Size.X, newHeight);
                _openPosition = newY;
                _closedPosition = _openPosition + newHeight - 50;
                _panelMoveDir = 0;
                GetViewport().SetInputAsHandled();
            }
            else
            {
                if (IsOverResizeHandle(motion.Position))
                    DisplayServer.CursorSetShape(DisplayServer.CursorShape.Vsize);
                else
                    DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
            }
        }
    }

    private bool IsOverResizeHandle(Vector2 globalMousePos)
    {
        var localPos = globalMousePos - GlobalPosition;
        return localPos.Y >= 0 && localPos.Y <= ResizeHandleHeight;
    }

    private const string OpenIcon = "res://Textures/UI/arrowup16.png";
    private const string CloseIcon = "res://Textures/UI/arrowdown16.png";

    private void TogglePanel()
    {
        _currentlyClosed = !_currentlyClosed;

        if (_currentlyClosed)
        {
            _openCloseButton.Icon = _openIcon;
            HidePanel();
        }
        else
        {
            _openCloseButton.Icon = _closeIcon;
            ShowPanel();
        }
    }

    private bool _currentlyClosed;

    private void OnWindowResized()
    {
        UpdatePositions();
        Position = new Vector2(0, _currentlyClosed ? _closedPosition : _openPosition);
        _targetPos = _currentlyClosed ? _closedPosition : _openPosition;
    }

    private void UpdatePositions()
    {
        _openPosition = GetViewport().GetVisibleRect().Size.Y - Size.Y;
        _closedPosition = _openPosition + Size.Y - 50;
    }

    public float HandY
    {
        get
        {
            if (_currentlyClosed) return _closedPosition;
            return _openPosition;
        }
    }

    private bool _locked;

    private void HidePanel()
    {
        _currentlyClosed = true;
        _targetPos = _closedPosition;
        _panelMoveDir = 1;
        Position = new Vector2(0, _closedPosition);
    }

    private void ShowPanel()
    {
        _targetPos = _openPosition;
        _currentlyClosed = false;
        _panelMoveDir = -1;
        Position = new Vector2(0, _openPosition);
    }

    [Export]
    private float _openSpeed = 1000;
    private int _panelMoveDir;
    private float _targetPos;

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        return;

        if (_panelMoveDir != 0)
        {
            var curPos = Position.Y;

            float newPos = curPos + _panelMoveDir * ((float)delta * _openSpeed);

            if (_panelMoveDir > 0)
                newPos = Math.Min(newPos, _targetPos);
            else
                newPos = Math.Max(newPos, _targetPos);

            Position = new Vector2(0, newPos);

            if (newPos == _targetPos)
                _panelMoveDir = 0;
        }
    }

    #region Drag-over preview (called from GameObjects during 3D drag)

    /// <summary>
    /// Called by GameObjects while a VcToken is being dragged and the mouse Y is over the hand panel.
    /// Shows a 2D preview image under the mouse cursor inside the panel.
    /// Pass null to hide the preview.
    /// </summary>
    public void ShowDragPreview(VcToken card, Vector2 globalMousePos)
    {
        if (card == null)
        {
            HideDragPreview();
            return;
        }

        if (card != _dragPreviewCard)
        {
            _dragPreviewCard = card;
            _dragPreview.Texture = GetCardTexture(card);
        }

        // Position the preview relative to this panel's local space
        var localPos = globalMousePos - GlobalPosition;
        _dragPreview.Position = localPos - _dragPreview.Size / 2f;
        _dragPreview.Visible = true;
    }

    public void HideDragPreview()
    {
        _dragPreview.Visible = false;
        _dragPreviewCard = null;
    }

    #endregion

    #region Hand Management

    /// <summary>
    /// Refreshes the local player's hand display from PlayerHandService.
    /// Shows the Face (front) of each card.
    /// </summary>
    public void RefreshDisplay()
    {
        _cards = PlayerHandService.Instance
            .GetHand(PlayerHandService.LocalSeatIndex())
            .ToList();

        MapHandToContainer();
    }

    public void AddToHand(VcToken card)
    {
        PlayerHandService.Instance?.AddCards(PlayerHandService.LocalSeatIndex(), new[] { card });
        // RefreshDisplay triggered by HandChangedEvent
    }

    public void AddToHand(IEnumerable<VcToken> cards)
    {
        PlayerHandService.Instance?.AddCards(PlayerHandService.LocalSeatIndex(), cards);
        // RefreshDisplay triggered by HandChangedEvent
    }

    public void RemoveFromHand(VcToken card)
    {
        PlayerHandService.Instance?.RemoveCard(card);
        card.Location = VisualComponentBase.ComponentLocation.Board;
        EventBus.Instance.Publish(new ReturnFromHandEvent { Card = card });
        // RefreshDisplay triggered by HandChangedEvent
    }

    private static ImageTexture GetCardTexture(VcToken card)
    {
        if (card.FaceSprite == null)
            return null;

        var fullImage = card.FaceSprite;
        if (fullImage.IsCompressed())
            fullImage.Decompress();

        int hframes = Math.Max(1, card.FaceHframes);
        int vframes = Math.Max(1, card.FaceVframes);
        int frameW = fullImage.GetWidth() / hframes;
        int frameH = fullImage.GetHeight() / vframes;
        int col = card.FaceFrame % hframes;
        int row = card.FaceFrame / hframes;
        var region = new Rect2I(col * frameW, row * frameH, frameW, frameH);
        var frameImage = fullImage.GetRegion(region);
        return ImageTexture.CreateFromImage(frameImage);
    }

    private void MapHandToContainer()
    {
        foreach (var c in _handContainer.GetChildren())
        {
            _handContainer.RemoveChild(c);
            c.QueueFree();
        }

        foreach (var card in _cards)
        {
            var t = new TextureRect();
            t.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            t.StretchMode = TextureRect.StretchModeEnum.Scale;
            t.Texture = GetCardTexture(card);

            // Allow dragging this card back to the board
            var capturedCard = card;
            t.GuiInput += (inputEvent) => OnCardGuiInput(inputEvent, capturedCard, t);

            _handContainer.AddChild(t);
        }
    }

    // Tracks which hand card is being dragged back to the board
    private VcToken _handDragCard;

    private void OnCardGuiInput(InputEvent inputEvent, VcToken card, TextureRect rect)
    {
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb && mb.Pressed)
        {
            // Remove from hand immediately and hand off to the 3D drag system.
            // OnReturnFromHand will set IsDragging = true so the normal HandleDrag loop takes over.
            _handDragCard = card;
            RemoveFromHand(card);
            GetViewport().SetInputAsHandled();
        }
    }

    #endregion
}


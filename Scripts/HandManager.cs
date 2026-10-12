using System;
using System.Collections.Generic;
using Godot;

public partial class HandManager : Panel
{
    private float _openPosition;
    private float _closedPosition;

    private Control _resizeHandle;
    private bool _isResizing;
    private float _resizeDragStartY;
    private float _resizePanelStartY;
    private float _resizePanelStartHeight;

    private Button _openCloseButton;

    private HandRow _hand;

    [Export]
    private Texture2D _openIcon;

    [Export]
    private Texture2D _closeIcon;

    // The scene's drag preview, which is duplicated when creating previews of held cards.
    private CardFace _dragPreview;

    // A preview for each card held over a hand, with the card and side it shows.
    private readonly List<(CardFace Rect, VcToken Card, bool Back)> _previews = new();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        UpdatePositions();
        Position = new Vector2(0, _openPosition);

        _openCloseButton = GetNode<Button>("HandLockButton");
        _openCloseButton.Pressed += TogglePanel;

        _resizeHandle = GetNode<Control>("ResizeHandle");
        _resizeHandle.GuiInput += OnResizeHandleGuiInput;

        GetTree().Root.SizeChanged += OnWindowResized;

        _hand = GetNode<HandRow>("%HandRow");
        _dragPreview = GetNode<CardFace>("%DragPreview");
        _previews.Add((_dragPreview, null, false));
    }

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
    }

    private void OnResizeHandleGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _isResizing = mb.Pressed;
            if (mb.Pressed)
            {
                _resizeDragStartY = mb.GlobalPosition.Y;
                _resizePanelStartY = Position.Y;
                _resizePanelStartHeight = Size.Y;
            }
            else
                UpdatePositions();
            _resizeHandle.AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion && _isResizing)
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
            _resizeHandle.AcceptEvent();
        }
    }

    private void TogglePanel()
    {
        _currentlyClosed = !_currentlyClosed;
        _openCloseButton.Icon = _currentlyClosed ? _openIcon : _closeIcon;
        Position = new Vector2(0, HandY);
    }

    private bool _currentlyClosed;

    private void OnWindowResized()
    {
        UpdatePositions();
        Position = new Vector2(0, HandY);
    }

    private void UpdatePositions()
    {
        _openPosition = GetViewport().GetVisibleRect().Size.Y - Size.Y;
        _closedPosition = _openPosition + Size.Y - 50;
    }

    private float HandY => _currentlyClosed ? _closedPosition : _openPosition;

    #region Drag-over preview (called from GameObjects during 3D drag)

    /// <summary>
    /// Shows <paramref name="cards"/> as they would be in <paramref name="row"/>, held around <paramref name="point"/>.
    /// </summary>
    /// <param name="row">The hand under the mouse, which gives the cards their scale.</param>
    /// <param name="point">The mouse point.</param>
    public void ShowDragPreview(IReadOnlyList<ComponentState> cards, HandRow row, Vector2 point)
    {
        var pointer = GetGlobalTransformWithCanvas().AffineInverse() * point;
        for (int i = 0; i < cards.Count; i++)
        {
            if (i == _previews.Count)
            {
                var copy = (CardFace)_dragPreview.Duplicate();
                AddChild(copy);
                _previews.Add((copy, null, false));
            }
            var s = cards[i];
            var card = ProjectService.Instance.GameObjects.GetComponent(s.Id) as VcToken;
            var (rect, shown, shownBack) = _previews[i];

            // A card can be flipped while it's held.
            bool back = HandCard.IsFaceDown(s);
            if (card != shown || back != shownBack)
            {
                rect.ShowCard(card, back);
                _previews[i] = (rect, card, back);
            }

            float rotation = HandCard.ScreenRotation(s.Rotation.Y);
            var (size, _) = row.CardSize(HandRow.TableSize(card), rotation);
            var offset = s.PositionAt(0);
            rect.Size = size;
            rect.PivotOffset = size / 2;
            rect.Rotation = rotation;
            rect.Position = pointer + new Vector2(offset.X, offset.Z) * row.PixelsPerCm - size / 2;
            rect.Visible = true;
        }
        for (int i = cards.Count; i < _previews.Count; i++)
            _previews[i].Rect.Visible = false;
    }

    public void HideDragPreview()
    {
        foreach (var preview in _previews)
            preview.Rect.Visible = false;
    }

    #endregion

    #region Hand Management

    private void Sync(IRecordReader R)
    {
        _hand.Seat = R.LocalSeat();
    }

    #endregion
}

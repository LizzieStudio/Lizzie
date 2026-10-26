using System;
using System.Collections.Generic;
using Godot;

namespace Lizzie;

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

    // A preview for each card held over a hand, with what it shows, or null when it's hidden.
    private readonly List<(
        CardFace Rect,
        (Prototype Prototype, PrototypeItemRef Item, bool Back)? Shown
    )> _previews = new();

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
        _previews.Add((_dragPreview, null));
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
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
    public void ShowDragPreview(IReadOnlyList<ComponentPosition> cards, HandRow row, Vector2 point)
    {
        var pointer = GetGlobalTransformWithCanvas().AffineInverse() * point;
        for (int i = 0; i < cards.Count; i++)
        {
            if (i == _previews.Count)
            {
                var copy = (CardFace)_dragPreview.Duplicate();
                AddChild(copy);
                _previews.Add((copy, null));
            }
            var s = cards[i];
            var (rect, shown) = _previews[i];

            // A card can be flipped, or its prototype edited, while it's held.
            var prototype = Atoms.R.GetPrototype(s);
            var shows = (prototype, Atoms.R.GetComponent(s).Item, HandCard.IsFaceDown(s));
            if (shows != shown)
            {
                rect.ShowCard(s, shows.Item3, Atoms.R);
                _previews[i] = (rect, shows);
            }

            float rotation = HandCard.ScreenRotation(s.Rotation.Y);
            var (size, _) = row.CardSize(
                HandRow.TableSize(prototype?.Parameters as PrintedParameters),
                rotation
            );
            var offset = s.PositionAt(0);
            rect.Size = size;
            rect.PivotOffset = size / 2;
            rect.Rotation = rotation;
            rect.Position = pointer + new Vector2(offset.X, offset.Z) * row.PixelsPerCm - size / 2;
            rect.Visible = true;
        }
        for (int i = cards.Count; i < _previews.Count; i++)
            HidePreview(i);
    }

    public void HideDragPreview()
    {
        for (int i = 0; i < _previews.Count; i++)
            HidePreview(i);
    }

    // Hides preview i and lets go of the faces it showed.
    private void HidePreview(int i)
    {
        var (rect, shown) = _previews[i];
        rect.Visible = false;
        if (shown == null)
            return;
        rect.ShowCard(null, false, Atoms.R);
        _previews[i] = (rect, null);
    }

    #endregion

    #region Hand Management

    private void Sync(IWatchReader R)
    {
        _hand.Seat = R.LocalSeat();
    }

    #endregion
}

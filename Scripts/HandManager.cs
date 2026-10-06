using System;
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

        _resizeHandle = GetNode<Control>("ResizeHandle");
        _resizeHandle.GuiInput += OnResizeHandleGuiInput;

        GetTree().Root.SizeChanged += OnWindowResized;

        var handContainer = GetNode<HBoxContainer>("%HandContainer");
        _hand = new HandRow();
        handContainer.AddChild(_hand);
        _hand.AddThemeConstantOverride("separation", handContainer.GetThemeConstant("separation"));

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

    private const string OpenIcon = "res://Textures/UI/arrowup16.png";
    private const string CloseIcon = "res://Textures/UI/arrowdown16.png";

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

    public float HandY => _currentlyClosed ? _closedPosition : _openPosition;

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
            _dragPreview.Texture = HandCard.CardTexture(card, back: false);
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

    private void Sync(IRecordReader R)
    {
        _hand.Seat = R.LocalSeat();
    }

    #endregion
}

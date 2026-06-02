using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        UpdatePositions();
        Position = new Vector2(0, _openPosition);
        //MouseEntered += ShowPanel;
        //MouseExited += HidePanel;

        _openCloseButton = GetNode<Button>("HandLockButton");
        _openCloseButton.Pressed += TogglePanel;
            
        GetTree().Root.SizeChanged += OnWindowResized;
        
        _handContainer = GetNode<HBoxContainer>("%HandContainer");

        EventBus.Instance.Subscribe<AddToHandEvent>(OnAddToHand);

        _openIcon = ResourceLoader.Load<Texture2D>(OpenIcon);
        _closeIcon = ResourceLoader.Load<Texture2D>(CloseIcon);

    }

    private void OnAddToHand(AddToHandEvent obj)
    {
        AddToHand(obj.Cards);
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
                // Update open position to reflect new size
                UpdatePositions();
                GetViewport().SetInputAsHandled();
                //DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
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

                // Clamp: panel must stay on screen and have a minimum height
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
                // Update cursor when hovering the resize handle
                if (IsOverResizeHandle(motion.Position))
                {
                    DisplayServer.CursorSetShape(DisplayServer.CursorShape.Vsize);
                    //GD.Print("VSize");
                }
                else
                {
                    DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
                    //GD.Print("Arrow");
                }
            }
        }
    }

    private bool IsOverResizeHandle(Vector2 globalMousePos)
    {
        var localPos = globalMousePos - GlobalPosition;
        //GD.Print(localPos.Y);
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
            {
                newPos = Math.Min(newPos, _targetPos);
            }
            else
            {
                newPos = Math.Max(newPos, _targetPos);
            }

            Position = new Vector2(0, newPos);

            if (newPos == _targetPos)
                _panelMoveDir = 0;
        }
    }

    #region Hand Management

    public void AddToHand(VcToken card)
    {
        _cards.Add(card);
        MapHandToContainer();
    }
    
    public void AddToHand(Guid cardId)
    {
        
    }
    
    public void AddToHand(IEnumerable<VcToken> cards)
    {
        foreach (var card in cards)
        {
            _cards.Add(card);
        }
        MapHandToContainer();
    }

    public void RemoveFromHand(VcToken card)
    {
        
    }

    public void RemoveFromHand(Guid card)
    {
        
    }

    private void MapHandToContainer()
    {
        //Clear out the old containers 
        //TODO - this is pretty inefficient, but we can optimize later if needed
        foreach(var c in _handContainer.GetChildren())
        {
            _handContainer.RemoveChild(c);
            c.QueueFree();
        }

        foreach (var card in _cards)
        {
            var t = new TextureRect();
            t.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            t.StretchMode = TextureRect.StretchModeEnum.Scale;
            t.MouseEntered += TOnMouseEntered;

            if (card.FaceSprite != null)
            {
                var fullImage = card.FaceSprite;
                if (fullImage.IsCompressed())
                {
                    fullImage.Decompress(); //GetRegion only works on decompressed images
                }
                int hframes = Math.Max(1, card.FaceHframes);
                int vframes = Math.Max(1, card.FaceVframes);
                int frameW = fullImage.GetWidth() / hframes;
                int frameH = fullImage.GetHeight() / vframes;
                int col = card.FaceFrame % hframes;
                int row = card.FaceFrame / hframes;
                var region = new Rect2I(col * frameW, row * frameH, frameW, frameH);
                var frameImage = fullImage.GetRegion(region);
                t.Texture = ImageTexture.CreateFromImage(frameImage);
            }


            _handContainer.AddChild(t);
        }
    }

    private void TOnMouseEntered()
    {
        GD.Print("Mouse Entered");
    }

    #endregion
}


using Godot;
using System;
using System.Diagnostics;

public partial class BoundsRect : MarginContainer
{
    private Button _topLeft;
    private Button _topRight;
    private Button _bottomLeft;
    private Button _bottomRight;
    private Button _topCenter;
    private Button _bottomCenter;
    private Button _middleLeft;
    private Button _middleRight;
    public ReferenceRect _outline;

    private float _timeElapsed = 0.0f;
    private bool _isFadingToTransparent = true;


    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _outline = GetNode<ReferenceRect>("ReferenceRect");

        _topLeft = GetNode<Button>("ReferenceRect/TopLeft");
        _topRight = GetNode<Button>("ReferenceRect/TopRight");
        _topCenter = GetNode<Button>("ReferenceRect/TopCenter");
        _middleLeft = GetNode<Button>("ReferenceRect/MiddleLeft");
        _middleRight = GetNode<Button>("ReferenceRect/MiddleRight");
        _bottomLeft = GetNode<Button>("ReferenceRect/BottomLeft");
        _bottomRight = GetNode<Button>("ReferenceRect/BottomRight");
        _bottomCenter = GetNode<Button>("ReferenceRect/BottomCenter");

        _topLeft.Pressed += () =>
        {
            _dragTop = true;
            _dragLeft = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _topCenter.Pressed += () =>
        {
            _dragTop = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _topRight.Pressed += () =>
        {
            _dragTop = true;
            _dragRight = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _middleLeft.Pressed += () =>
        {
            _dragLeft = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _middleRight.Pressed += () =>
        {
            _dragRight = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _bottomLeft.Pressed += () =>
        {
            _dragBottom = true;
            _dragLeft = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _bottomCenter.Pressed += () =>
        {
            _dragBottom = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };

        _bottomRight.Pressed += () =>
        {
            _dragBottom = true;
            _dragRight = true;
            _dragging = true;
            _dragLast = GetViewport().GetMousePosition();
        };
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        if (_dragging)
        {
            ProcessDrag();
        }
        else
        {
            if (Input.IsMouseButtonPressed(MouseButton.Left))
            {
                if (!_dragOutside)
                {
                    var _mousePos = GetLocalMousePosition();

                    //check to see if mouse is within marquee. If so, drag the whole thing
                    var rect = new Rect2(_outline.Position, _outline.Size);

                    if (rect.HasPoint(_mousePos))
                    {
                        _dragLast = GetViewport().GetMousePosition();
                        _dragTop = true;
                        _dragBottom = true;
                        _dragLeft = true;
                        _dragRight = true;
                        _dragging = true;
                    }
                    else
                    {
                        _dragOutside = true;
                    }
                }
            }
            else
            {
                _dragOutside = false;
            }
        }

        // Update the elapsed time
        _timeElapsed += (float)delta;

        // Calculate the new alpha value over a 2-second period (1-second fade in/out)
        float alpha = _isFadingToTransparent
            ? 1.0f - (_timeElapsed / 2.0f) // Fade out (alpha decreases)
            : _timeElapsed / 2.0f; // Fade in (alpha increases)

        // Clamp the alpha value between 0 (fully transparent) and 1 (fully opaque)
        alpha = Mathf.Clamp(alpha, 0.0f, 1.0f);

        // Get the current modulate color and set the new alpha value
        _outline.BorderColor = Colors.Red.Lerp(Colors.Yellow, alpha);

        // If 2 seconds have passed, reverse the fade direction and reset the timer
        if (_timeElapsed >= 2.0f)
        {
            _timeElapsed = 0.0f;
            _isFadingToTransparent = !_isFadingToTransparent;
        }
    }

    private bool _dragging;
    private bool _dragTop;
    private bool _dragBottom;
    private bool _dragLeft;
    private bool _dragRight;
    private Vector2 _dragLast;

    private int _startMarginTop;
    private int _startMarginBottom;
    private int _startMarginLeft;
    private int _startMarginRight;

    private bool
        _dragOutside; //the user started dragging outside the marquee, so don't do anything even if the mouse moves inside

    private void ProcessDrag()
    {
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragging = false;
            _dragTop = false;
            _dragBottom = false;
            _dragLeft = false;
            _dragRight = false;
        }
        else
        {
            //process the drag
            var mouseDelta = GetViewport().GetMousePosition() - _dragLast;

            if (_dragTop) DragTop(mouseDelta);
            if (_dragBottom) DragBottom(mouseDelta);
            if (_dragLeft) DragLeft(mouseDelta);
            if (_dragRight) DragRight(mouseDelta);

            _dragLast = GetViewport().GetMousePosition();
            
            RaiseBoundsChanged();
        }
    }

    private void DragTop(Vector2 mouseDelta)
    {
        var ot = GetThemeConstant("margin_top");
        AddThemeConstantOverride("margin_top", ot + (int)mouseDelta.Y);
    }

    private void DragBottom(Vector2 mouseDelta)
    {
        var ob = GetThemeConstant("margin_bottom");
        AddThemeConstantOverride("margin_bottom", ob - (int)mouseDelta.Y);
    }

    private void DragLeft(Vector2 mouseDelta)
    {
        var ol = GetThemeConstant("margin_left");
        AddThemeConstantOverride("margin_left", ol + (int)mouseDelta.X);
    }

    private void DragRight(Vector2 mouseDelta)
    {
        var or = GetThemeConstant("margin_right");
        AddThemeConstantOverride("margin_right", or - (int)mouseDelta.X);
    }
    
    public event EventHandler BoundsChanged;

    private void RaiseBoundsChanged()
    {
        BoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetBounds(Rect2I rect, TextureContext context)
    {
        //change from center origin to top left origin
        rect.Position = new(rect.Position.X - rect.Size.X / 2, rect.Position.Y - rect.Size.Y / 2);
        
        AddThemeConstantOverride("margin_left", rect.Position.X);
        AddThemeConstantOverride("margin_top", rect.Position.Y);
        AddThemeConstantOverride("margin_right", (int)context.ParentSize.X - rect.Position.X - rect.Size.X);
        AddThemeConstantOverride("margin_bottom", (int)context.ParentSize.Y - rect.Position.Y - rect.Size.Y);
    }

    public (int l, int t, int r, int b) GetBounds()
    {
        return (
            GetThemeConstant("margin_left"), 
            GetThemeConstant("margin_top"), 
            GetThemeConstant("margin_right"),
            GetThemeConstant("margin_bottom"));
    }
}
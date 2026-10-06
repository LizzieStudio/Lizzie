using System.Collections.Generic;
using Godot;

public abstract partial class BaseCamera : Node3D, ICamera
{
    [Export]
    protected GameObjects _gameObjects;

    [Export]
    protected float RotationSpeed = 1;

    [Export]
    protected float ZoomSpeed = 2;

    [Export]
    protected float ContinuousZoomSpeed = 10;

    [Export]
    protected float PanSpeed = 10;

    private Transform3D _initialTransform;
    private Transform3D _initialCameraTransform;

    public bool Current
    {
        get => ActualCamera.Current;
        set => ActualCamera.Current = value;
    }

    protected Camera3D ActualCamera { get; private set; }

    public override void _Ready()
    {
        base._Ready();
        ActualCamera = GetCameraNode();
        _initialTransform = Transform;
        _initialCameraTransform = ActualCamera.Transform;
        EventBus.Instance.Subscribe<ModalDialogOpenedEvent>(OnModalOpened);
        EventBus.Instance.Subscribe<ModalDialogClosedEvent>(OnModalClosed);
    }

    private void OnModalClosed()
    {
        _modalOpen = false;
    }

    private bool _modalOpen;

    private void OnModalOpened()
    {
        _modalOpen = true;
        _held.Clear();
        EndRotate();
    }

    private bool AcceptsInput =>
        !_modalOpen
        && Current
        && _gameObjects.CursorMode != CursorMode.DragSelect
        && !CommandMenu.IsOpen;

    /// <summary>The keys that move the camera while held.</summary>
    private static readonly StringName[] HeldActions =
    [
        "zoom_in",
        "zoom_out",
        "pan_left",
        "pan_right",
        "pan_up",
        "pan_down",
        "reset_view",
    ];

    /// <summary>
    /// The held actions, like holding W to move the camera forward.
    /// </summary>
    private readonly HashSet<StringName> _held = new();

    private bool Held(StringName action) => _held.Contains(action);

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!AcceptsInput)
            return;

        // Handle Zoom
        if (Held("zoom_in"))
            UpdateZoom((float)-delta * ContinuousZoomSpeed);
        if (Held("zoom_out"))
            UpdateZoom((float)delta * ContinuousZoomSpeed);

        // Handle Pan
        var pan = new Vector2(
            (Held("pan_right") ? 1 : 0) - (Held("pan_left") ? 1 : 0),
            (Held("pan_down") ? 1 : 0) - (Held("pan_up") ? 1 : 0)
        );
        UpdatePan(pan.LimitLength() * (float)delta);

        // Reset
        if (Held("reset_view"))
            Reset();
    }

    // Only the main viewport's shortcuts reach here, so inputs targeting a dialogue never move the camera.
    public override void _UnhandledKeyInput(InputEvent e)
    {
        foreach (var action in HeldActions)
            if (Shortcuts.Pressed(e, action))
                _held.Add(action);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            _held.Clear();
            EndRotate();
        }
    }

    public override void _Input(InputEvent e)
    {
        base._Input(e);

        // Releasing a key is always visible, regardless of propogation.
        foreach (var action in HeldActions)
            if (e.IsActionReleased(action))
                _held.Remove(action);

        // _rotateFrom != null
        if (_rotateFrom is not { } from)
            return;

        if (e is InputEventMouseButton && e.IsActionReleased("rotate"))
        {
            if (_rotating)
                GetViewport().SetInputAsHandled();
            EndRotate();
        }
        else if (e is InputEventMouseMotion motion)
        {
            if (_rotating)
            {
                UpdateRotation(motion.Relative);
                GetViewport().SetInputAsHandled();
            }
            // A right click opens the command menu, so wait until a drag threshold is passed.
            else if (motion.Position.DistanceTo(from) >= GetViewport().GuiDragThreshold)
            {
                _rotating = true;
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!AcceptsInput || e is not InputEventMouseButton button)
            return;

        if (e.IsActionPressed("rotate"))
            _rotateFrom = button.Position;
        if (e.IsActionPressed("zoom_in"))
            UpdateZoom(-ZoomSpeed);
        if (e.IsActionPressed("zoom_out"))
            UpdateZoom(ZoomSpeed);
    }

    // Where the rotate click started.
    private Vector2? _rotateFrom;

    private bool _rotating;

    /// <summary>
    /// Stops rotating and puts the cursor back where the drag began.
    /// </summary>
    private void EndRotate()
    {
        if (_rotating)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().WarpMouse(_rotateFrom!.Value);
        }
        _rotating = false;
        _rotateFrom = null;
    }

    protected abstract Camera3D GetCameraNode();

    protected abstract void UpdateZoom(float zoomValue);
    protected abstract void UpdateRotation(Vector2 mousePosition);
    protected abstract void ZoomComponent(VisualComponentBase component);

    /// <summary>Frames the component.</summary>
    public void ZoomTo(VisualComponentBase component) => ZoomComponent(component);

    protected virtual void Reset()
    {
        Transform = _initialTransform;
        ActualCamera.Transform = _initialCameraTransform;
    }

    private void UpdatePan(Vector2 direction)
    {
        TranslateObjectLocal(new Vector3(direction.X * PanSpeed, 0, direction.Y * PanSpeed));
    }
}

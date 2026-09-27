using System.Collections.Generic;
using System.ComponentModel;
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
    }

    private bool AcceptsInput =>
        !_modalOpen
        && Current
        && _gameObjects.CursorMode != CursorMode.DragSelect
        && _gameObjects.CursorMode != CursorMode.PopupMenu;

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
            _held.Clear();
    }

    // Controls that use the mouse are better handled in the _Input method because of engine quirks (like Mouse wheel not having a pressed event).
    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        // Releasing a key is always visible, regardless of propogation.
        foreach (var action in HeldActions)
            if (@event.IsActionReleased(action))
                _held.Remove(action);

        if (
            !Current
            || _gameObjects.CursorMode == CursorMode.DragSelect
            || _gameObjects.CursorMode == CursorMode.PopupMenu
        )
            return;

        // defer to ImGui debuggers
        if (ImGuiInterop.ClaimingMouse)
            return;

        if (Input.IsActionPressed("rotate") && @event is InputEventMouseMotion mouseMotion)
        {
            UpdateRotation(mouseMotion.Relative);
        }

        if (@event is InputEventMouseButton)
        {
            if (@event.IsActionPressed("zoom_in"))
                UpdateZoom(-ZoomSpeed);
            if (@event.IsActionPressed("zoom_out"))
                UpdateZoom(ZoomSpeed);
        }
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

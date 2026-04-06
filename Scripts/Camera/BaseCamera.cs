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
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_modalOpen)
            return;

        if (
            !Current
            || _gameObjects.CursorMode == CursorMode.DragSelect
            || _gameObjects.CursorMode == CursorMode.PopupMenu
        )
            return;

        // Handle Zoom
        if (Input.IsActionPressed("zoom_in"))
            UpdateZoom((float)-delta * ContinuousZoomSpeed);
        if (Input.IsActionPressed("zoom_out"))
            UpdateZoom((float)delta * ContinuousZoomSpeed);
        if (Input.IsActionJustPressed("component_zoom"))
            ZoomComponent(_gameObjects.GetMouseSelectedObject());

        // Handle Pan
        UpdatePan(Input.GetVector("pan_left", "pan_right", "pan_up", "pan_down") * (float)delta);

        // Reset
        if (Input.IsActionPressed("reset_view"))
            Reset();
    }

    // Controls that use the mouse a better handled in the _Input method because of engine quirks (like Mouse wheel not having a pressed event).
    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        if (
            !Current
            || _gameObjects.CursorMode == CursorMode.DragSelect
            || _gameObjects.CursorMode == CursorMode.PopupMenu
        )
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

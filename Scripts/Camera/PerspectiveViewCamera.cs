using Godot;

public partial class PerspectiveViewCamera : BaseCamera
{
    [Export]
    private float MinZoomDistance = 2;

    [Export]
    private float MaxZoomDistance = 110;

    private Node3D _gimbal;
    private Transform3D _initialGimbalTransform;

    private float _total_pitch;

    public override void _Ready()
    {
        base._Ready();
        _gimbal = GetNode<Node3D>("Gimbal");
        _initialGimbalTransform = _gimbal.Transform;
        Reset();
    }

    protected override Camera3D GetCameraNode()
    {
        return GetNode<Camera3D>("Gimbal/Camera");
    }

    protected override void UpdateRotation(Vector2 mousePosition)
    {
        // Yaw
        RotateY(Mathf.DegToRad(mousePosition.X * RotationSpeed));

        // Pitch
        var pitch = Mathf.Clamp(mousePosition.Y * RotationSpeed, -_total_pitch, 90 - _total_pitch);
        _total_pitch += pitch;
        _gimbal.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-pitch));
    }

    protected override void UpdateZoom(float zoomValue)
    {
        ActualCamera.Position = new Vector3(0, 0, Mathf.Clamp(ActualCamera.Position.Z + zoomValue, MinZoomDistance, MaxZoomDistance));
    }

    protected override void ZoomComponent(VisualComponentBase component)
    {
        if (component == null) return;
        GlobalPosition = new Vector3(component.Position.X, 0, component.Position.Z);
        ActualCamera.Position = new Vector3(0, 0, Mathf.Clamp(component.MaxAxisSize * 1.2f, MinZoomDistance, MaxZoomDistance));
    }

    protected override void Reset()
    {
        base.Reset();
        _gimbal.Transform = _initialGimbalTransform;
        _total_pitch = -_gimbal.RotationDegrees.X;
    }
}

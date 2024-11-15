using Godot;

public partial class TopViewCamera : BaseCamera
{
    [Export]
    private float MinSize = 2;

    [Export]
    private float MaxSize = 110;


    protected override Camera3D GetCameraNode()
    {
        return GetNode<Camera3D>("Camera");
    }

    protected override void UpdateRotation(Vector2 mousePosition)
    {
        RotateY(Mathf.DegToRad(-mousePosition.X * RotationSpeed));
    }

    protected override void UpdateZoom(float zoomValue)
    {
        ActualCamera.Size = Mathf.Clamp(ActualCamera.Size + zoomValue, MinSize, MaxSize);
    }

    protected override void ZoomComponent(VisualComponentBase component)
    {
        if (component == null) return;
        Position = new Vector3(component.Position.X, Position.Y, component.Position.Z);
        ActualCamera.Size = Mathf.Clamp(component.MaxAxisSize * 1.2f, MinSize, MaxSize);
    }

    protected override void Reset()
    {
        base.Reset();
        ActualCamera.Size = MaxSize;
    }
}

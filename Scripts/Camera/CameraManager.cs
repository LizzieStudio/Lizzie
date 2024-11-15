using Godot;

public partial class CameraManager : Node3D
{
    private ICamera _perspectiveViewCamera;
    private ICamera _topViewCamera;

    public override void _Ready()
    {
        base._Ready();
        _perspectiveViewCamera = GetNode<ICamera>("PerspectiveViewCamera");
        _topViewCamera = GetNode<ICamera>("TopViewCamera");
        SetTopView();
    }

    public void SetPerspectiveView()
    {
        _perspectiveViewCamera.Current = true;
    }

    public void SetTopView()
    {
        _topViewCamera.Current = true;
    }
}

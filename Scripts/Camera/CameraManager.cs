using System;
using Godot;

namespace Lizzie;

public partial class CameraManager : Node3D
{
    private ICamera _perspectiveViewCamera;
    private ICamera _topViewCamera;

    public static CameraManager Instance { get; private set; }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        base._Ready();
        _perspectiveViewCamera = GetNode<ICamera>("PerspectiveViewCamera");
        _topViewCamera = GetNode<ICamera>("TopViewCamera");
        SetMode(Config.Registry.Get<SceneMode>("SceneMode"));
    }

    public void SetMode(SceneMode mode)
    {
        var camera = mode switch
        {
            SceneMode.TwoD => _topViewCamera,
            SceneMode.ThreeD => _perspectiveViewCamera,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
        camera.Current = true;
    }

    /// <summary>Moves the current camera to frame the component.</summary>
    public void ZoomTo(VisualComponentBase component)
    {
        var camera = _topViewCamera.Current ? _topViewCamera : _perspectiveViewCamera;
        (camera as BaseCamera)?.ZoomTo(component);
    }
}

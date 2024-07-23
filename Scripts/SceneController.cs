using Godot;
using System;

public partial class SceneController : Node3D
{
	private Camera3D _2dCamera;
	private Camera3D _3dCamera;
	
	
	public enum SceneMode {TwoD, ThreeDFixed, ThreeDPhysics, Creator}

	public override void _Ready()
	{
		_3dCamera = GetNode<Camera3D>("CameraBase/Camera3D");
		_2dCamera = GetNode<Camera3D>("Pseudo2DCamera");
	}

	public virtual void SetMode(SceneMode mode)
	{
		switch (mode)
		{
			case SceneMode.TwoD:
				_2dCamera.Current = true;
				break;
			case SceneMode.ThreeDFixed:
				_3dCamera.Current = true;
				break;
			case SceneMode.ThreeDPhysics:
				break;
			case SceneMode.Creator:
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
		}
	}
}

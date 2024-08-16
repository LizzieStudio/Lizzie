using Godot;
using System;

public partial class SceneController : Node3D
{
	private Pseudo2DCamera _2dCamera;
	private CameraController _3dCamera;
	
	
	public enum SceneMode {TwoD, ThreeDFixed, ThreeDPhysics, Creator}

	public override void _Ready()
	{
		_3dCamera = GetNode<CameraController>("CameraBase");
		_2dCamera = GetNode<Pseudo2DCamera>("Pseudo2DCamera");
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
	
	public void EnterSpawnMode(VisualComponentBase component)
	{
		_2dCamera.EnterSpawnMode(component);
	}

	public void ExitSpawnMode()
	{
		_2dCamera.ExitSpawnMode();
	}

	public void TestFunction()
	{
		var p = GetNode<Pseudo2DCamera>("Pseudo2DCamera");
		p.CollisionTest();
	}
}

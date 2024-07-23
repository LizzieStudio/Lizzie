using Godot;
using System;

public partial class GameController : Node3D
{
	private SceneController _mainScene;

	private UI _uiController;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_mainScene = GetNode<SceneController>("3DSceneNoPhysics");
		_mainScene.SetMode(SceneController.SceneMode.TwoD);

		_uiController = GetNode<UI>("UI");
		_uiController.MasterModeChange += OnMasterModeChange;
	}

	private void OnMasterModeChange(object sender, MasterModeChangeArgs e)
	{
		switch (e.NewMode)
		{
			case UI.MasterMode.TwoD:
				_mainScene.SetMode(SceneController.SceneMode.TwoD);
				break;
			case UI.MasterMode.ThreeD:
				_mainScene.SetMode(SceneController.SceneMode.ThreeDFixed);
				break;
			case UI.MasterMode.Designer:
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}

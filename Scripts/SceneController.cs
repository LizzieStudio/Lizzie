using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class SceneController : Node3D
{
    [Export]
    TextureFactory _textureFactory;

    private CameraManager _cameraManager;
    private GameObjects _gameObjects;

    public override void _Ready()
    {
        _cameraManager = GetNode<CameraManager>("Cameras");
        _gameObjects = GetNode<GameObjects>("GameObjects");
        SetMode(Config.Registry.Get<SceneMode>("SceneMode"));
        _gameObjects.TextureFactory = _textureFactory;

        PresenceSynchronizer.Instance?.SetContext(GetNode<DragPlane>("DragPlane"));

        CommandList.Register(CommandList.All.Concat(_gameObjects.Commands));
        ShortcutRelay.Install(GetTree());
        ClickRouting.Install(GetTree().Root);
        // The table takes the main window's commands.
        CommandViews.Attach(GetTree().Root, _gameObjects);
    }

    public override void _ExitTree()
    {
        PresenceSynchronizer.Instance?.ClearContext();
    }

    public GameObjects GameObjects => _gameObjects;

    public TextureFactory TextureFactory => _textureFactory;

    #region Message to/from Game
    public virtual void SetMode(SceneMode mode)
    {
        switch (mode)
        {
            case SceneMode.TwoD:
                _cameraManager.SetTopView();
                break;
            case SceneMode.ThreeDFixed:
                _cameraManager.SetPerspectiveView();
                break;
            case SceneMode.ThreeDPhysics:
                break;
            case SceneMode.Creator:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    public void EnterSpawnMode(List<(VisualComponentBase Component, Vector3 Delta)> components)
    {
        _gameObjects.EnterSpawnMode(components);
    }

    #endregion
}

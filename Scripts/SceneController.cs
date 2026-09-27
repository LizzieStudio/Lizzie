using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

public partial class SceneController : Node3D, ICommandView
{
    [Signal]
    public delegate void ShowComponentPopupEventHandler(
        Vector2I position,
        Godot.Collections.Array<VisualComponentBase> components
    );

    [Export]
    TextureFactory _textureFactory;

    private CameraManager _cameraManager;
    private GameObjects _gameObjects;

    private VcMeeple _meepletest;

    private Sprite3D _testSprite;

    public override void _Ready()
    {
        _cameraManager = GetNode<CameraManager>("Cameras");
        _gameObjects = GetNode<GameObjects>("GameObjects");
        SetMode(Config.Registry.Get<SceneMode>("SceneMode"));
        _gameObjects.ShowComponentPopup += GameObjectsOnShowComponentPopup;
        _gameObjects.HoveredComponentChange += OnHoveredComponentChange;
        _gameObjects.TextureFactory = _textureFactory;

        PresenceSynchronizer.Instance?.SetContext(GetNode<DragPlane>("DragPlane"), this);

        ShortcutRelay.Install(GetTree());
        // The table takes the main window's commands.
        CommandViews.Attach(GetTree().Root, this);
    }

    public override void _ExitTree()
    {
        PresenceSynchronizer.Instance?.ClearContext();
        CommandViews.Detach(GetTree().Root);
    }

    private void OnHoveredComponentChange(object sender, HoveredComponentChangeEventArgs e)
    {
        HoveredComponentChange?.Invoke(this, e);
    }

    public event EventHandler<HoveredComponentChangeEventArgs> HoveredComponentChange;

    public GameObjects GameObjects => _gameObjects;

    /// <summary>
    /// Build a context menu for the selected components or the hovered component.
    /// Add in the prototypes as secondary context.
    /// </summary>
    public CommandContext BuildContext()
    {
        // While dragging a selection box, don't build any context.
        if (_gameObjects.CursorMode == CursorMode.DragSelect)
            return null;

        var primary = _gameObjects
            .GetPrimaryObjects()
            .Select(c => new RecordTarget(c.Reference));

        var secondary = primary
            .Select(t => ProjectService.Instance.Get<ComponentState>(t.Id)?.PrototypeRef ?? SnowTag.Empty)
            .Where(id => id != SnowTag.Empty)
            .Select(p => new RecordTarget(p.Value));

        return new CommandContext { Primary = primary.ToImmutableHashSet<Target>(), Secondary = secondary.ToImmutableHashSet<Target>() };
    }

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

    public void TestFunction() { }

    private void TextureDone(ImageTexture obj)
    {
        var d = obj.GetImage();
        //d.SavePng(@"c:\winwam5\tfTest.png");
    }

    public void EnterSpawnMode(List<(VisualComponentBase Component, Vector3 Delta)> components)
    {
        _gameObjects.EnterSpawnMode(components);
    }

    public void PopupClosed()
    {
        _gameObjects.PopupClosed();
    }

    private void OnShowComponentPopup(
        Vector2I position,
        Godot.Collections.Array<VisualComponentBase> components
    )
    {
        //EmitSignal(SignalName.ShowComponentPopup, position, components);
    }

    private void GameObjectsOnShowComponentPopup(object sender, ShowComponentPopupEventArgs e)
    {
        ShowComponentPopup2?.Invoke(this, e);
    }

    public event EventHandler<ShowComponentPopupEventArgs> ShowComponentPopup2;
    #endregion
}

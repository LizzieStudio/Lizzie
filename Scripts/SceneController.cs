using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class SceneController : Node3D, ICommandView
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

        CommandList.Register(CommandList.All.Concat(TableCommands));
        ShortcutRelay.Install(GetTree());
        ClickRouting.Install(GetTree().Root);
        // The table takes the main window's commands.
        CommandViews.Attach(GetTree().Root, this);
    }

    public override void _ExitTree()
    {
        PresenceSynchronizer.Instance?.ClearContext();
    }

    public GameObjects GameObjects => _gameObjects;

    #region ICommandView

    /// <summary>
    /// The table's selection is in the stage it shows.
    /// </summary>
    public SnowTag SelectionScope(IAtomReader R) => _gameObjects.ShownStage(R);

    /// <summary>
    /// The hovered component, which keys act on when nothing is selected.
    /// None while drawing a selection box or placing new components.
    /// </summary>
    public IEnumerable<AtomRef> Hovered() =>
        _gameObjects.CursorMode is CursorMode.DragSelect or CursorMode.Spawn
        || _gameObjects.GetHoveredObject() is not { } hovered
        || hovered.State == null
            ? []
            : [ComponentState.ById.Ref(hovered.Key)];

    public IReadOnlyList<Command> Commands => TableCommands;

    /// <summary>
    /// The drag Id for attaching events to its undo group.
    /// </summary>
    public SnowportId Gesture => _gameObjects.DragGroup;

    #endregion

    /// <summary>
    /// The table undoes the components of the stage it shows, and what's selected there.
    /// </summary>
    public bool UndoScope(Atom atom)
    {
        var shown = _gameObjects.ShownStage(AtomService.Instance);
        return atom switch
        {
            ComponentState s => s.Stage == shown,
            Selection s => s.Within == shown,
            _ => false,
        };
    }

    #region Table commands
    // These act on the table rather than on atoms, so only the table offers them.

    private static readonly Command DuplicateComponent = new AtomCommand<ComponentState>
    {
        Name = new("table.duplicate"),
        // The prototype manifest's clone icon.
        Icon = CommandIcons.Duplicate,
        Caption = "Duplicate Component",
        Count = TargetCount.One,
        // Picks up a copy to place, as if spawning its prototype.
        SideEffects = (cs, _) =>
            EventBus.Instance.Publish(
                new SpawnPrototypeEvent { PrototypeRef = cs[0].PrototypeRef, Card = cs[0].Card }
            ),
    };

    private static readonly Command ZoomToComponent = new AtomCommand<ComponentState>
    {
        Name = new("table.zoom"),
        Icon = CommandIcons.Zoom,
        Caption = "Zoom to Component",
        Keys = [Shortcuts.Key(Key.Z)],
        Count = TargetCount.One,
        AppliesTo = (R, c) => !R.IsContained(c) || R.IsInDeck(c),
        SideEffects = (cs, _) =>
        {
            if (ProjectService.Instance.GameObjects.GetComponent(cs[0].Id) is { } node)
                CameraManager.Instance?.ZoomTo(node);
        },
    };

    // After the commands, since static fields are set in order.
    private static readonly IReadOnlyList<Command> TableCommands =
    [
        DuplicateComponent,
        ZoomToComponent,
    ];
    #endregion

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

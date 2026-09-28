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

        CommandList.Register(CommandList.All.Concat(TableCommands));
        ShortcutRelay.Install(GetTree());
        ClickRouting.Install(GetTree().Root);
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
    /// The context for the selected components, or the hovered component.
    /// Their contents, like a deck's cards, come along, and their prototypes are referenced.
    /// </summary>
    public CommandContext BuildContext()
    {
        // While dragging a selection box, don't build any context.
        if (_gameObjects.CursorMode == CursorMode.DragSelect)
            return null;

        var R = ProjectService.Instance;
        var selected = _gameObjects
            .GetTargetedObjects()
            .Select(c => R.Get<ComponentState>(c.Reference))
            .Where(s => s != null)
            .ToList();

        return new CommandContext
        {
            Selected = selected.Select(s => new RecordTarget(s.Id)).ToImmutableHashSet<Target>(),
            Contents = selected
                .SelectMany(s => R.Contents(s))
                .Select(s => new RecordTarget(s.Id))
                .ToImmutableHashSet<Target>(),
            Referenced = selected
                .Select(s => new RecordTarget(s.PrototypeRef))
                .ToImmutableHashSet<Target>(),
            Local = TableCommands,
        };
    }

    /// <summary>
    /// The table undoes its components, and what's selected on it.
    /// A snapshot switch is included through the components it writes.
    /// </summary>
    public bool UndoScope(Effect fx) =>
        fx switch
        {
            UpdateReplicatedEffect<ComponentState> => true,
            UpdateReplicatedEffect<Selection> s => s.Payload?.Within == SnowTag.Empty,
            _ => false,
        };

    #region Table commands
    // These act on the table rather than on records, so only the table offers them.

    private static readonly Command DuplicateComponent = new RecordCommand<ComponentState>
    {
        Id = new("table.duplicate"),
        // The prototype manifest's clone icon.
        Icon = UI.TextureUI_ContentCopy,
        Caption = "Duplicate Component",
        Count = TargetCount.One,
        // Picks up a copy to place, as if spawning its prototype.
        SideEffects = (cs, _) =>
            EventBus.Instance.Publish(
                new SpawnPrototypeEvent
                {
                    PrototypeRef = cs[0].PrototypeRef,
                    DataSetRowIndex = cs[0].DataSetRowIndex,
                    DataSetRowId = cs[0].DataSetRowId,
                }
            ),
    };

    private static readonly Command ZoomToComponent = new RecordCommand<ComponentState>
    {
        Id = new("table.zoom"),
        Icon = "res://Textures/UI/zoom.svg",
        Caption = "Zoom to Component",
        Keys = [Shortcuts.Key(Key.Z)],
        Count = TargetCount.One,
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

public partial class SceneController : Node3D
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
        Shortcuts.Undo += IssueUndo;
        Shortcuts.Redo += IssueRedo;
    }

    public override void _ExitTree()
    {
        PresenceSynchronizer.Instance?.ClearContext();
        Shortcuts.Undo -= IssueUndo;
        Shortcuts.Redo -= IssueRedo;
    }

    private void OnHoveredComponentChange(object sender, HoveredComponentChangeEventArgs e)
    {
        HoveredComponentChange?.Invoke(this, e);
    }

    public event EventHandler<HoveredComponentChangeEventArgs> HoveredComponentChange;

    public GameObjects GameObjects => _gameObjects;

    /// <summary>False while drag selecting or while a context menu is open.</summary>
    private bool AcceptsShortcuts =>
        _gameObjects.CursorMode != CursorMode.DragSelect
        && _gameObjects.CursorMode != CursorMode.PopupMenu;

    /// <summary>Issues an undo.</summary>
    private void IssueUndo()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        // Nothing is undone in the middle of a gesture.
        if (log == null || EventSynchronizer.Instance.InGroup || !AcceptsShortcuts)
            return;
        using var _ = DebugTimings.Measure("Undo");
        if (UndoLog.ComputeUndoTarget(log, Snowport.Clock.source) is SnowportId target)
            EventSynchronizer.Instance.Submit(TableEvent.Now(new UndoAction { Target = target }));
    }

    /// <summary>Issues a redo, which is just an undo targeting the most recent active undo.</summary>
    private void IssueRedo()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        // Nothing is undone in the middle of a gesture.
        if (log == null || EventSynchronizer.Instance.InGroup || !AcceptsShortcuts)
            return;
        using var _ = DebugTimings.Measure("Redo");
        if (UndoLog.ComputeRedoTarget(log, Snowport.Clock.source) is SnowportId target)
            EventSynchronizer.Instance.Submit(
                TableEvent.Now(new UndoAction { Target = target, Redo = true })
            );
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

    #region Commands
    public void SendCommandToSelected(VisualCommand command)
    {
        SendCommandToComponents(command, _gameObjects.GetSelectedObjects());
    }

    public void SendCommandToComponents(
        VisualCommand command,
        IEnumerable<VisualComponentBase> components
    )
    {
        var effects = new List<Effect>();
        foreach (var c in components)
            effects.AddRange(c.ProcessCommand(command));

        EventSynchronizer.Instance?.Submit(TableEvent.Now(ActionFor(command), effects.ToArray()));
    }

    /// <summary>
    /// Sends a command with an associated quantity to each component.
    /// Each component receives ProcessCommandWithQuantity; if it doesn't override that,
    /// we fall back to the Num1–Num5 VisualCommands for quantities 1–5, or the
    /// base command for quantities > 5 (treated as "all" by the component).
    /// </summary>
    public void SendCommandToComponentsWithQuantity(
        VisualCommand command,
        IEnumerable<VisualComponentBase> components,
        int quantity
    )
    {
        var effects = new List<Effect>();
        foreach (var c in components)
            effects.AddRange(c.ProcessCommandWithQuantity(command, quantity));

        EventSynchronizer.Instance?.Submit(TableEvent.Now(ActionFor(command), effects.ToArray()));
    }

    private static TableAction ActionFor(VisualCommand command)
    {
        // Number keys draw that many cards off a deck, which still works.
        // TODO We need to decouple drawing cards from setting the die face somehow.
        if ((int)command >= (int)VisualCommand.Num1 && (int)command <= (int)VisualCommand.Num20)
            return new DrawAction();

        return command switch
        {
            VisualCommand.Flip => new FlipAction(),
            VisualCommand.Roll => new RollAction(),
            VisualCommand.Shuffle => new ShuffleAction(),
            VisualCommand.Draw => new DrawAction(),
            VisualCommand.Deal => new DealAction(),
            _ => null,
        };
    }

    /// <summary>The table's shortcuts and the command they send.</summary>
    private static readonly (StringName Action, VisualCommand Command)[] CommandShortcuts =
    [
        ("flip", VisualCommand.Flip),
        ("num_1", VisualCommand.Num1),
        ("num_2", VisualCommand.Num2),
        ("num_3", VisualCommand.Num3),
        ("num_4", VisualCommand.Num4),
        ("num_5", VisualCommand.Num5),
        ("num_6", VisualCommand.Num6),
        ("num_7", VisualCommand.Num7),
        ("num_8", VisualCommand.Num8),
        ("num_9", VisualCommand.Num9),
        ("num_10", VisualCommand.Num10),
        ("num_11", VisualCommand.Num11),
        ("num_12", VisualCommand.Num12),
        ("num_13", VisualCommand.Num13),
        ("num_14", VisualCommand.Num14),
        ("num_15", VisualCommand.Num15),
        ("num_16", VisualCommand.Num16),
        ("num_17", VisualCommand.Num17),
        ("num_18", VisualCommand.Num18),
        ("num_19", VisualCommand.Num19),
        ("num_20", VisualCommand.Num20),
        ("roll", VisualCommand.Roll),
        ("rotate_cw", VisualCommand.RotateCw),
        ("rotate_ccw", VisualCommand.RotateCcw),
    ];

    // _ShortcutInput only runs for shortcuts made on the table, so a focused dialog doesn't trigger this.
    public override void _ShortcutInput(InputEvent e)
    {
        if (!AcceptsShortcuts)
            return;

        foreach (var (action, command) in CommandShortcuts)
        {
            if (Shortcuts.Pressed(e, action))
            {
                SendCommandToSelected(command);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }
    #endregion
}

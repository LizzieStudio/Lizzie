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
    private Table _table;

    private VcMeeple _meepletest;

    private Sprite3D _testSprite;

    public override void _Ready()
    {
        _cameraManager = GetNode<CameraManager>("Cameras");
        _gameObjects = GetNode<GameObjects>("GameObjects");
        _table = GetNode<Table>("Table");
        SetMode(Config.Registry.Get<SceneMode>("SceneMode"));
        _gameObjects.ShowComponentPopup += GameObjectsOnShowComponentPopup;
        _gameObjects.HoveredComponentChange += OnHoveredComponentChange;
        _gameObjects.TextureFactory = _textureFactory;

        PresenceSynchronizer.Instance?.SetContext(GetNode<DragPlane>("DragPlane"), this);
    }

    public override void _ExitTree()
    {
        PresenceSynchronizer.Instance?.ClearContext();
    }

    private void OnHoveredComponentChange(object sender, HoveredComponentChangeEventArgs e)
    {
        HoveredComponentChange?.Invoke(this, e);
    }

    public event EventHandler<HoveredComponentChangeEventArgs> HoveredComponentChange;

    public GameObjects GameObjects => _gameObjects;

    /// <summary>The rendered table surface.</summary>
    public Table Table => _table;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (
            _gameObjects.CursorMode != CursorMode.DragSelect
            && _gameObjects.CursorMode != CursorMode.PopupMenu
        )
        {
            CheckForCommands();
            if (Input.IsActionJustPressed("ui_undo"))
                IssueUndo();
            if (Input.IsActionJustPressed("ui_redo"))
                IssueRedo();
        }
    }

    /// <summary>Issues an undo.</summary>
    private void IssueUndo()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        if (log == null)
            return;
        if (UndoLog.ComputeUndoTarget(log, Snowport.Clock.source) is SnowportId target)
            EventSynchronizer.Instance.Submit(TableEvent.Now(new UndoAction { Target = target }));
    }

    /// <summary>Issues a redo, which is just an undo targeting the most recent active undo.</summary>
    private void IssueRedo()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        if (log == null)
            return;
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

    public void EnterSpawnMode(List<VisualComponentBase> components)
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

    private void CheckForCommands()
    {
        if (Input.IsActionJustPressed("flip"))
            SendCommandToSelected(VisualCommand.Flip);

        if (Input.IsActionJustPressed("num_1"))
            SendCommandToSelected(VisualCommand.Num1);
        if (Input.IsActionJustPressed("num_2"))
            SendCommandToSelected(VisualCommand.Num2);
        if (Input.IsActionJustPressed("num_3"))
            SendCommandToSelected(VisualCommand.Num3);
        if (Input.IsActionJustPressed("num_4"))
            SendCommandToSelected(VisualCommand.Num4);
        if (Input.IsActionJustPressed("num_5"))
            SendCommandToSelected(VisualCommand.Num5);
        if (Input.IsActionJustPressed("num_6"))
            SendCommandToSelected(VisualCommand.Num6);
        if (Input.IsActionJustPressed("num_7"))
            SendCommandToSelected(VisualCommand.Num7);
        if (Input.IsActionJustPressed("num_8"))
            SendCommandToSelected(VisualCommand.Num8);
        if (Input.IsActionJustPressed("num_9"))
            SendCommandToSelected(VisualCommand.Num9);
        if (Input.IsActionJustPressed("num_10"))
            SendCommandToSelected(VisualCommand.Num10);
        if (Input.IsActionJustPressed("num_11"))
            SendCommandToSelected(VisualCommand.Num11);
        if (Input.IsActionJustPressed("num_12"))
            SendCommandToSelected(VisualCommand.Num12);
        if (Input.IsActionJustPressed("num_13"))
            SendCommandToSelected(VisualCommand.Num13);
        if (Input.IsActionJustPressed("num_14"))
            SendCommandToSelected(VisualCommand.Num14);
        if (Input.IsActionJustPressed("num_15"))
            SendCommandToSelected(VisualCommand.Num15);
        if (Input.IsActionJustPressed("num_16"))
            SendCommandToSelected(VisualCommand.Num16);
        if (Input.IsActionJustPressed("num_17"))
            SendCommandToSelected(VisualCommand.Num17);
        if (Input.IsActionJustPressed("num_18"))
            SendCommandToSelected(VisualCommand.Num18);
        if (Input.IsActionJustPressed("num_19"))
            SendCommandToSelected(VisualCommand.Num19);
        if (Input.IsActionJustPressed("num_20"))
            SendCommandToSelected(VisualCommand.Num20);

        if (Input.IsActionJustPressed("roll"))
            SendCommandToSelected(VisualCommand.Roll);
        if (Input.IsActionJustPressed("rotate_cw"))
            SendCommandToSelected(VisualCommand.RotateCw);
        if (Input.IsActionJustPressed("rotate_ccw"))
            SendCommandToSelected(VisualCommand.RotateCcw);
    }
    #endregion
}

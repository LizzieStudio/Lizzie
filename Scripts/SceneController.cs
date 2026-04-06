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
        _gameObjects.ShowComponentPopup += GameObjectsOnShowComponentPopup;
        _gameObjects.HoveredComponentChange += OnHoveredComponentChange;
        _gameObjects.TextureFactory = _textureFactory;
    }

    private void OnHoveredComponentChange(object sender, HoveredComponentChangeEventArgs e)
    {
        HoveredComponentChange?.Invoke(this, e);
    }

    public event EventHandler<HoveredComponentChangeEventArgs> HoveredComponentChange;

    public GameObjects GameObjects => _gameObjects;

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
                UndoService.Instance.Undo();
        }
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
        d.SavePng(@"c:\winwam5\tfTest.png");
    }

    public void EnterSpawnMode(VisualComponentBase component)
    {
        _gameObjects.EnterSpawnMode(component);
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
    public bool SendCommandToSelected(VisualCommand command)
    {
        return SendCommandToComponents(command, _gameObjects.GetSelectedObjects());
    }

    public bool SendCommandToComponents(
        VisualCommand command,
        IEnumerable<VisualComponentBase> components
    )
    {
        bool result = false;

        Update update = new();

        foreach (var c in components)
        {
            var change = c.ProcessCommand(command);
            if (!change.Consumed)
                continue;
            if (change.UndoAction != null)
                update.Add(change.UndoAction);
            result = true;
        }

        if (update.Count > 0)
        {
            UndoService.Instance.Add(update);
        }

        return result;
    }

    private void CheckForCommands()
    {
        if (Input.IsActionJustPressed("flip"))
            SendCommandToSelected(VisualCommand.Flip);
        if (Input.IsActionJustPressed("lock"))
            SendCommandToSelected(VisualCommand.ToggleLock);

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
    }
    #endregion
}

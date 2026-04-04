using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class GameController : Node3D
{
    private SceneController _mainScene;

    private UI _uiController;

    [Export]
    private TextureFactory _textureFactory;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _mainScene = GetNode<SceneController>("3DSceneNoPhysics");
        _mainScene.SetMode(SceneMode.TwoD);
        _mainScene.ShowComponentPopup2 += MainSceneOnShowComponentPopup2;
        _mainScene.HoveredComponentChange += MainSceneOnHoveredNameChange;
        _mainScene.GameObjects.TextureFactory = _textureFactory;

        _uiController = GetNode<UI>("UI");
        _uiController.MasterModeChange += OnMasterModeChange;
        _uiController.CreateObject += OnCreateObject;
        _uiController.SetGameController(this);

        ProjectService.Instance.CurrentProject = ProjectService.Instance.LoadProject("TestProject");
        ProjectService.Instance.GameObjects = _mainScene.GameObjects;

        var commandDic = new CommandDictionary(_mainScene);
    }

    private void MainSceneOnShowComponentPopup2(object sender, ShowComponentPopupEventArgs e)
    {
        ShowComponentPopup(e.Position, e.Components);
    }

    private void MainSceneOnHoveredNameChange(object sender, HoveredComponentChangeEventArgs e)
    {
        _uiController.UpdateHoveredName(e.Component);
    }

    private void OnCreateObject(object sender, CreateObjectEventArgs args)
    {
        VisualComponentBase component = SpawnComponent(args.PrototypeName);

        if (component == null)
        {
            GD.PrintErr("Null Spawn Component");
            return;
        }

        component.PrototypeRef = args.PrototypeRef;
        component.ExcludeFromSync = true;

        //if the name is blank in the parameters, set it
        if (args.Params.ContainsKey("ComponentName") && args.Params.ContainsKey("BaseName"))
        {
            if (string.IsNullOrWhiteSpace(args.Params["ComponentName"].ToString()))
            {
                args.Params["ComponentName"] = _mainScene.GameObjects.CreateUniqueName(
                    args.Params["BaseName"].ToString()
                );
            }
        }

        //Add to Prototype Manifest if it's not already there
        ProjectService.Instance.AddPrototypeToManifest(args);

        if (component.Build(args.PrototypeRef, _textureFactory))
        {
            _mainScene.EnterSpawnMode(component);
        }
        else
        {
            GD.PrintErr("Error building component");
        }
    }

    private void OnMasterModeChange(object sender, MasterModeChangeArgs e)
    {
        switch (e.NewMode)
        {
            case UI.MasterMode.TwoD:
                _mainScene.SetMode(SceneMode.TwoD);
                break;
            case UI.MasterMode.ThreeD:
                _mainScene.SetMode(SceneMode.ThreeDFixed);
                break;
            case UI.MasterMode.Designer:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    public SceneController MainScene => _mainScene;

    public VisualComponentBase SpawnComponent(string prototype)
    {
        var scene = ResourceLoader.Load<PackedScene>(prototype).Instantiate();

        if (scene is VisualComponentBase vcb)
        {
            return vcb;
        }
        return null;
    }

    public void ShowComponentPopup(Vector2I position, IEnumerable<VisualComponentBase> selected)
    {
        _uiController.BuildPopupMenu(selected.ToList());
        _uiController.ShowComponentPopup(position);
    }

    /*
    public void HideComponentPopup()
    {
        _uiController.HideComponentPopup();
    }
*/

    public void ComponentPopupClosed()
    {
        _mainScene.PopupClosed();
    }

    public bool ProcessPopupCommand(VisualCommand command, List<VisualComponentBase> components)
    {
        var result = _mainScene.SendCommandToComponents(command, components);
        ComponentPopupClosed();
        return result;
    }

    //test function
    public void TestFunction()
    {
        _mainScene.TestFunction();
    }
}

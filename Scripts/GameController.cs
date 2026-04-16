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
        _mainScene.ShowComponentPopup2 += MainSceneOnShowComponentPopup2;
        _mainScene.HoveredComponentChange += MainSceneOnHoveredNameChange;
        _mainScene.GameObjects.TextureFactory = _textureFactory;

        _uiController = GetNode<UI>("UI");
        _uiController.SceneModeChange += OnSceneModeChange;
        _uiController.CreateObject += OnCreateObject;
        _uiController.SetGameController(this);

        ProjectService.Instance.CurrentProject =
            ProjectService.Instance.LoadProject(ProjectService.SampleProjectName)
            ?? new Project { Name = ProjectService.SampleProjectName };
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
        //Add to Prototype Manifest if it's not already there
        ProjectService.Instance.AddPrototypeToManifest(args);

        var components = new List<VisualComponentBase>();

        if (args.MultipleCreateMode)
        {
            int cols = (int)Math.Ceiling(Math.Sqrt(args.DataSet.Rows.Count));
            
            int i = 0;
            int j = 0;

            float w = args.WidthHint * 1.5f;
            float h = args.HeightHint * 1.5f;

            foreach (var r in args.DataSet.Rows)
            {
                var mc = SingleComponentSpawn(args, r.Key);

                if (mc != null)
                {
                    mc.SpawnDelta = new Vector3(w * i, 0, h * j);
                    i++;
                    if (i == cols)
                    {
                        i = 0;
                        j++;
                    }

                    components.Add(mc);
                }
            }
        }
        else
        {
            var sc = SingleComponentSpawn(args, string.Empty);
            if (sc != null) components.Add(sc);
        }

        _mainScene.EnterSpawnMode(components);

    }

    private VisualComponentBase SingleComponentSpawn(CreateObjectEventArgs args, string row)
    {
        VisualComponentBase component = SpawnComponent(args.PrototypeName);

        if (component == null)
        {
            GD.PrintErr("Null Spawn Component");
            return null;
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

        if (component.Build(args.PrototypeRef, row, _textureFactory))
        {
            return component;
        }
        else
        {
            GD.PrintErr("Error building component");
            return null;
        }

    }

    private void OnSceneModeChange(object sender, SceneModeChangeArgs e)
    {
        switch (e.NewMode)
        {
            case SceneMode.TwoD:
                _mainScene.SetMode(SceneMode.TwoD);
                break;
            case SceneMode.ThreeDFixed:
                _mainScene.SetMode(SceneMode.ThreeDFixed);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        Config.Registry.Set("SceneMode", e.NewMode);
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

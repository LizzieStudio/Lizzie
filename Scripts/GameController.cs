using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
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

        EventBus.Instance.Subscribe<SpawnPrototypeEvent>(OnSpawnPrototype);

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
            var mode = Utility.GetParam<VcToken.TokenBuildMode>(args.Params, "Mode");
            if (mode == VcToken.TokenBuildMode.Grid)
            {
                SpawnGridMultiples(args, components);
            }
            else
            {
                SpawnDataSetMultiples(args, components);
            }
        }
        else
        {
            var sc = SingleComponentSpawn(args, string.Empty);
            if (sc != null)
                components.Add(sc);
        }

        _mainScene.EnterSpawnMode(components);
    }

    private void OnSpawnPrototype(SpawnPrototypeEvent e)
    {
        if (
            !ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(
                e.PrototypeRef,
                out var prototype
            )
        )
        {
            GD.PrintErr($"SpawnPrototype: prototype {e.PrototypeRef} not found");
            return;
        }

        var scenePath = Utility.ComponentTypeToScenePath(prototype.Type, prototype.Parameters);
        if (string.IsNullOrEmpty(scenePath))
        {
            GD.PrintErr($"SpawnPrototype: could not resolve scene path for {prototype.Type}");
            return;
        }

        var args = new CreateObjectEventArgs
        {
            ComponentType = prototype.Type,
            Params = new Dictionary<string, object>(prototype.Parameters),
            PrototypeRef = prototype.PrototypeRef,
            PrototypeName = scenePath,
        };

        var component = SingleComponentSpawn(args, string.Empty);
        if (component != null)
        {
            _mainScene.EnterSpawnMode(new List<VisualComponentBase> { component });
        }
    }

    private void SpawnGridMultiples(
        CreateObjectEventArgs args,
        List<VisualComponentBase> components
    )
    {
        var gridRows = Utility.GetParam<int>(args.Params, "GridRows");
        var gridCols = Utility.GetParam<int>(args.Params, "GridCols");
        var cardCount = Utility.GetParam<int>(args.Params, "GridCount");

        float w = args.WidthHint * 1.5f;
        float h = args.HeightHint * 1.5f;

        //we map the tokens into as much of a square as possible, regardless of the grid dims.
        int cols = (int)Math.Ceiling(Math.Sqrt(cardCount));
        int ci = 0;
        int cj = 0;

        int cardNum = 0;

        for (int i = 0; i < gridRows; i++)
        for (int j = 0; j < gridCols; j++)
        {
            var mc = SingleComponentSpawn(args, cardNum.ToString());

            if (mc != null)
            {
                mc.SpawnDelta = new Vector3(w * ci, 0, h * cj);
                components.Add(mc);
            }

            cardNum++;
            if (cardNum >= cardCount)
                return;

            ci++;
            if (ci == cols)
            {
                ci = 0;
                cj++;
            }
        }
    }

    private void SpawnDataSetMultiples(
        CreateObjectEventArgs args,
        List<VisualComponentBase> components
    )
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

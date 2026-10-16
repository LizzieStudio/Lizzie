using System;
using System.Collections.Generic;
using Godot;

public partial class GameController : Node
{
    private SceneController _mainScene;

    private UI _uiController;

    [Export]
    private TextureFactory _textureFactory;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _mainScene = GetNode<SceneController>("3DSceneNoPhysics");
        _mainScene.GameObjects.HoveredComponentChange += MainSceneOnHoveredNameChange;
        _mainScene.GameObjects.TextureFactory = _textureFactory;
        _mainScene.GameObjects.SetGameController(this);

        _uiController = GetNode<UI>("UI");
        _uiController.SceneModeChange += OnSceneModeChange;

        EventBus.Instance.Subscribe<SpawnPrototypeEvent>(OnSpawnPrototype);

        ProjectService.Instance.GameObjects = _mainScene.GameObjects;

        if (ProjectService.Instance.LoadProject(ProjectService.SampleProjectName) == null)
            ProjectService.Instance.NewGame(ProjectService.SampleProjectName);
    }

    private void MainSceneOnHoveredNameChange(object sender, HoveredComponentChangeEventArgs e)
    {
        _uiController.UpdateHoveredName(e.Component);
    }

    private void OnSpawnPrototype(SpawnPrototypeEvent e)
    {
        var prototype = RecordService.Instance.Get(Prototype.ById, e.PrototypeRef);
        if (prototype == null)
        {
            GD.PrintErr($"SpawnPrototype: prototype {e.PrototypeRef} not found");
            return;
        }

        var components = new List<(VisualComponentBase, Vector3)>();

        if (
            e.AllRows
            && prototype.Parameters is DieParameters { Faces: TemplateSide side } die
            && RecordService.Instance.Get(DataSet.ById, side.Dataset) != null
        )
        {
            SpawnRows(prototype, side.Dataset, die.Size / 10f, components);
        }
        else
        {
            var card =
                e.Card != PrototypeItemRef.None || prototype.Parameters is DeckParameters
                    ? e.Card
                    : PrototypeItemRef.First(prototype.Parameters, RecordService.Instance);
            var component = SingleComponentSpawn(prototype, card);
            if (component != null)
                components.Add((component, Vector3.Zero));
        }

        if (components.Count > 0)
            _mainScene.EnterSpawnMode(components);
    }

    private void SpawnRows(
        Prototype prototype,
        SnowTag dataset,
        float size,
        List<(VisualComponentBase, Vector3)> components
    )
    {
        var rows = RecordService.Instance.GetRows(dataset);
        int cols = (int)Math.Ceiling(Math.Sqrt(rows.Count));

        int i = 0;
        int j = 0;

        float spacing = size * 1.5f;

        for (int n = 0; n < rows.Count; n++)
        {
            var mc = SingleComponentSpawn(
                prototype,
                new PrototypeItemRef(n, rows[n].Id, SnowTag.Empty)
            );

            if (mc != null)
            {
                components.Add((mc, new Vector3(spacing * i, 0, spacing * j)));
                i++;
                if (i == cols)
                {
                    i = 0;
                    j++;
                }
            }
        }
    }

    private VisualComponentBase SingleComponentSpawn(Prototype prototype, PrototypeItemRef card)
    {
        var scenePath = Utility.ComponentTypeToScenePath(
            prototype.Type,
            prototype.Parameters,
            RecordService.Instance,
            card != PrototypeItemRef.None
        );
        if (string.IsNullOrEmpty(scenePath))
        {
            GD.PrintErr($"SpawnPrototype: could not resolve scene path for {prototype.Type}");
            return null;
        }

        VisualComponentBase component = ProjectService.Instance.SpawnComponent(scenePath);

        if (component == null)
        {
            GD.PrintErr("Null Spawn Component");
            return null;
        }

        component.PrototypeRef = prototype.Id;
        component.Card = card;

        component.TextureFactory = _textureFactory;
        return component;
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

    public HandManager HandManager => _uiController.HandManager;
}

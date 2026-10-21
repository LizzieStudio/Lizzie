using System;
using System.Collections.Generic;
using Godot;

namespace Lizzie;

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
        var prototype = AtomService.Instance.Get(Prototype.ById, e.PrototypeRef);
        if (prototype == null)
        {
            GD.PrintErr($"SpawnPrototype: prototype {e.PrototypeRef} not found");
            return;
        }

        var components = new List<(VisualComponentBase, Vector3)>();

        if (
            e.AllRows
            && prototype.Parameters is DieParameters { Faces: TemplateSide side } die
            && AtomService.Instance.Get(DataSet.ById, side.Dataset) != null
        )
        {
            SpawnRows(prototype, side, die.Size / 10f, components);
        }
        else if (
            e.Item == null
            && prototype.Parameters is PrintedParameters { SpawnDeck: false } printed
        )
        {
            SpawnStack(prototype, printed, components);
        }
        else
        {
            // A printed prototype spawns its deck as no item, and the deck brings its cards.
            var item =
                e.Item != null || prototype.Parameters is PrintedParameters
                    ? e.Item
                    : PrototypeItemRef.First(prototype.Parameters, AtomService.Instance);
            var component = SingleComponentSpawn(prototype, item);
            if (component != null)
                components.Add((component, Vector3.Zero));
        }

        if (components.Count > 0)
            _mainScene.EnterSpawnMode(components);
    }

    private void SpawnRows(
        Prototype prototype,
        TemplateSide side,
        float size,
        List<(VisualComponentBase, Vector3)> components
    )
    {
        var R = AtomService.Instance;
        int count = side.Count(R);
        int cols = (int)Math.Ceiling(Math.Sqrt(count));

        int i = 0;
        int j = 0;

        float spacing = size * 1.5f;

        for (int n = 0; n < count; n++)
        {
            var mc = SingleComponentSpawn(prototype, new DieItemRef(side.Row(n, R)));

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

    /// <summary>
    /// Every card of <paramref name="printed"/> face-up in one stack, the first on top.
    /// A prototype with no cards spawns one blank card.
    /// </summary>
    private void SpawnStack(
        Prototype prototype,
        PrintedParameters printed,
        List<(VisualComponentBase, Vector3)> components
    )
    {
        var R = AtomService.Instance;
        int count = Math.Max(printed.CardCount(R), 1);
        float thickness = VcPrinted.ThicknessOf(printed);

        // Components are written bottom first, so the last card goes first.
        for (int n = count - 1; n >= 0; n--)
        {
            var card = SingleComponentSpawn(prototype, printed.Item(n, R));
            if (card != null)
                components.Add((card, new Vector3(0, thickness * (count - 1 - n), 0)));
        }
    }

    private VisualComponentBase SingleComponentSpawn(Prototype prototype, PrototypeItemRef item)
    {
        var scenePath = Utility.ComponentTypeToScenePath(
            prototype.Type,
            prototype.Parameters,
            AtomService.Instance,
            item != null
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
        component.Item = item;

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

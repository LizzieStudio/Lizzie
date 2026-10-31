using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class GameController : Node
{
    private GameObjects _gameObjects;

    private UI _uiController;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _gameObjects = GetNode<GameObjects>("3DSceneNoPhysics");
        _gameObjects.HoveredComponentChange += OnHoveredComponentChange;

        _uiController = GetNode<UI>("UI");
        _uiController.SceneModeChange += OnSceneModeChange;

        EventBus.Instance.Subscribe<SpawnPrototypeEvent>(OnSpawnPrototype);

        ProjectService.Instance.GameObjects = _gameObjects;

        var args = OS.GetCmdlineUserArgs();
        // a launch argument used to skip having to open a project manually
        int at = Array.IndexOf(args, "--project") + 1;
        string name = at > 0 && at < args.Length ? args[at] : null;
        Project project = null;
        try
        {
            if (name != null)
            {
                project =
                    ProjectService.Instance.LoadProject(name, out var warning)
                    ?? throw new ProjectLoadException(
                        $"There's no saved project called \"{name}\"."
                    );
                if (warning != null)
                    _uiController.ShowLoadMessage("Older Save Opened", warning);
            }
        }
        catch (ProjectLoadException error)
        {
            _uiController.ShowLoadMessage("Can't Open Project", error.Message);
        }
        if (project == null)
            ProjectService.Instance.NewGame();
    }

    private void OnHoveredComponentChange(object sender, HoveredComponentChangeEventArgs e)
    {
        _uiController.UpdateHoveredName(e.Component);
    }

    private void OnSpawnPrototype(SpawnPrototypeEvent e)
    {
        var prototype = Atoms.R.Get(Prototype.ById, e.PrototypeRef);
        if (prototype == null)
        {
            GD.PrintErr($"SpawnPrototype: prototype {e.PrototypeRef} not found");
            return;
        }

        var components = new List<(VisualComponentBase, Vector3)>();

        if (
            e.AllRows
            && prototype.Parameters is DieParameters { Faces: TemplateSide side } die
            && Atoms.R.Get(Dataset.ById, side.DatasetRef) != null
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
                    : PrototypeItemRef.All(prototype.Parameters, Atoms.R).FirstOrDefault();
            var component = SingleComponentSpawn(prototype, item);
            if (component != null)
                components.Add((component, Vector3.Zero));
        }

        if (components.Count > 0)
            _gameObjects.EnterSpawnMode(components);
    }

    private void SpawnRows(
        Prototype prototype,
        TemplateSide side,
        float size,
        List<(VisualComponentBase, Vector3)> components
    )
    {
        var rows = side.Rows(Atoms.R);
        int count = rows.Count;
        int cols = (int)Math.Ceiling(Math.Sqrt(count));

        int i = 0;
        int j = 0;

        float spacing = size * 1.5f;

        for (int n = 0; n < count; n++)
        {
            var mc = SingleComponentSpawn(prototype, new DieItemRef(rows[n]));

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
        int count = Math.Max(printed.CardCount(Atoms.R), 1);
        float thickness = VcPrinted.ThicknessOf(printed);

        // Components are written bottom first, so the last card goes first.
        for (int n = count - 1; n >= 0; n--)
        {
            var card = SingleComponentSpawn(prototype, printed.Item(n, Atoms.R));
            if (card != null)
                components.Add((card, new Vector3(0, thickness * (count - 1 - n), 0)));
        }
    }

    private VisualComponentBase SingleComponentSpawn(Prototype prototype, PrototypeItemRef item)
    {
        var scenePath = Utility.ComponentTypeToScenePath(
            prototype.Type,
            prototype.Parameters,
            Atoms.R,
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
        return component;
    }

    private void OnSceneModeChange(object sender, SceneModeChangeArgs e)
    {
        CameraManager.Instance.SetMode(e.NewMode);
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }
}

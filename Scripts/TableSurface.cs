using Godot;

namespace Lizzie;

public partial class TableSurface : StaticBody3D
{
    private MeshInstance3D _tableMesh;
    private PlaneMesh _mesh;

    // the game's surface, from the scene
    private StandardMaterial3D _playMaterial;

    // a blue grid, for blueprints
    private ShaderMaterial _blueprintMaterial;

    // which blueprint the table shows, counting from the root
    private Label3D _blueprintNumber;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _tableMesh = GetNode<MeshInstance3D>("%TableMesh");
        _mesh = _tableMesh.Mesh as PlaneMesh;
        _playMaterial = (StandardMaterial3D)_mesh.Material;
        _blueprintMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/blueprint_table.gdshader"),
        };
        _blueprintNumber = GetNode<Label3D>("%BlueprintNumber");
    }

    public override void _EnterTree()
    {
        AtomService.Instance.Watch(this, Sync);
    }

    private void Sync(IWatchReader R)
    {
        var s = R.Get(ProjectGameSettings.Only);

        _mesh.Size =
            s.TableUnits == 0 // feet
                ? new Vector2(s.TableWidth, s.TableHeight) * 12f * 2.54f // convert to cm
                : new Vector2(s.TableWidth, s.TableHeight) * 100f; // table is in cm

        _playMaterial.AlbedoColor = s.TableColor;

        // The grid is spaced in centimeters, so it needs the table's size.
        _blueprintMaterial.SetShaderParameter("table_size_cm", _mesh.Size);

        // A blueprint shows as a blue grid with its number, and the game as its table.
        var game = R.Get(CurrentGame.Only).StageRef;
        var shown = ProjectService.Instance.GameObjects.ShownStage(R);
        var isGame = shown == game;
        _mesh.Material = isGame ? _playMaterial : _blueprintMaterial;
        _blueprintNumber.Visible = !isGame;
        // Numbered like the stage buttons, the root blueprint first.
        _blueprintNumber.Text = (
            ProjectService
                .Instance.GameObjects.PickableStages(R)
                .FindIndex(stage => stage.Id == shown) + 1
        ).ToString();
    }
}

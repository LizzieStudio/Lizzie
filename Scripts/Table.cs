using Godot;

public partial class Table : StaticBody3D
{
    private MeshInstance3D _tableMesh;
    private PlaneMesh _mesh;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _tableMesh = GetNode<MeshInstance3D>("%TableMesh");
        _mesh = _tableMesh.Mesh as PlaneMesh;
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        var s = R.Value<ProjectGameSettings>();

        _mesh.Size =
            s.TableUnits == 0 // feet
                ? new Vector2(s.TableWidth, s.TableHeight) * 12f * 2.54f // convert to cm
                : new Vector2(s.TableWidth, s.TableHeight) * 100f; // table is in cm

        if (_mesh.Material is StandardMaterial3D mat)
        {
            mat.AlbedoColor = s.TableColor;
        }
    }
}

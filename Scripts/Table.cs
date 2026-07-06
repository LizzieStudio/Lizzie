using Godot;
using System;

public partial class Table : StaticBody3D
{
    private MeshInstance3D _tableMesh;
    private PlaneMesh _mesh;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_tableMesh = GetNode<MeshInstance3D>("%TableMesh");
		_mesh = _tableMesh.Mesh as PlaneMesh;
        _mesh.Size = _size;
        
        EventBus.Instance.Subscribe<ProjectSettingsChangedEvent>(OnProjectSettingsChanged);
    }

    private void OnProjectSettingsChanged()
    {
        var s = ProjectService.Instance.CurrentProject.GameSettings;
        if (s.TableUnits == 0)  //feet
        {
            SetTableSize(new Vector2(s.TableWidth, s.TableHeight) * 12f * 2.54f);   //convert to cm
        }
        else
        {
            SetTableSize(new Vector2(s.TableWidth, s.TableHeight) * 100f);  //table is in cm
        }

        var color = new Color(s.TableColorR, s.TableColorG, s.TableColorB, s.TableColorA);
        if (_mesh.Material is StandardMaterial3D mat)
        {
            mat.AlbedoColor = color;
        }
    }

    private Vector2 _size = new Vector2(100, 100);

    public void SetTableSize(Vector2 size)
    {
        if (_size == size) return;
        
		if (IsNodeReady())
        {
            _mesh.Size = size;
        }
        else
        {
            _size = size;
        }
    }
}

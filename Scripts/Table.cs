using System;
using Godot;

public partial class Table : StaticBody3D
{
    private MeshInstance3D _tableMesh;
    private PlaneMesh _mesh;
    private Material _playMat;
    private ShaderMaterial _blueprintMat;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _tableMesh = GetNode<MeshInstance3D>("%TableMesh");
        _mesh = _tableMesh.Mesh as PlaneMesh;
        _mesh.Size = _size;
        _playMat = _mesh.Material; // the scene's original green table

        var shader = GD.Load<Shader>("res://Shaders/blueprint_table.gdshader");
        _blueprintMat = new ShaderMaterial { Shader = shader };
        PushSizeToShader(_mesh.Size);

        _mesh.Material = _playMat;

        EventBus.Instance.Subscribe<ProjectSettingsChangedEvent>(OnProjectSettingsChanged);
    }

    private void OnProjectSettingsChanged()
    {
        var s = ProjectService.Instance.CurrentProject.GameSettings;
        Vector2 sizeCm =
            s.TableUnits == 0 // feet
                ? new Vector2(s.TableWidth, s.TableHeight) * 12f * 2.54f // convert to cm
                : new Vector2(s.TableWidth, s.TableHeight) * 100f; // table is in cm

        SetTableSize(sizeCm);
        PushSizeToShader(sizeCm);

        if (_playMat is StandardMaterial3D mat)
        {
            mat.AlbedoColor = s.TableColor;
        }
    }

    private void PushSizeToShader(Vector2 sizeCm) =>
        _blueprintMat?.SetShaderParameter("table_size", sizeCm);

    /// <summary>true shows a blueprint table, which will be used for edit mode.</summary>
    public void SetEditMode(bool editing) => _mesh.Material = editing ? _blueprintMat : _playMat;

    private Vector2 _size = new Vector2(100, 100);

    public void SetTableSize(Vector2 size)
    {
        if (_size == size)
            return;

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

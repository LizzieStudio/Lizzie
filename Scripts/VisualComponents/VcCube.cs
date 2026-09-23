using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;

public partial class VcCube : VisualComponentBase
{
    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Cube;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
    }

    protected override bool Setup(ComponentParameters parameters, IRecordReader R)
    {
        base.Setup(parameters, R);
        var p = (CubeParameters)parameters;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        if (p.Height <= 0)
            return false;

        Height = p.Height / 10f;
        Width = p.Width / 10f;
        Length = p.Length / 10f;
        CubeColor = p.Color;

        //create cube
        if (Width <= 0 || Length <= 0)
        {
            Scale = new Vector3(Height, Height, Height);
        }
        else
        {
            Scale = new Vector3(Width, Height, Length);
        }

        YHeight = Height;

        SetColor(CubeColor);

        var r = new RectangleShape2D();
        r.Size = new Vector2(Width, Length);

        ShapeProfiles.Add(new OffsetShape2D(r));

        return true;
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Math.Max(Height, Width), Length);

    private float Height;
    private float Width;
    private float Length;
    private Color CubeColor;
}

public sealed record CubeParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Cube;

    public float Height { get; init; }
    public float Width { get; init; }
    public float Length { get; init; }
    public Color Color { get; init; } = Colors.Black;
}

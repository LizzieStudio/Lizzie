using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;

public partial class VcDisc : VisualComponentBase
{
    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Disc;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
    }

    public override bool Setup(
        ComponentParameters parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        base.Setup(parameters, dataSetRow, textureFactory);
        var p = (DiscParameters)parameters;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        if (p.Height <= 0)
            return false;

        Height = p.Height / 10f;
        Diameter = p.Diameter / 10f;
        DiscColor = p.Color;

        //create cylinder
        Scale = new Vector3(Diameter, Height, Diameter);

        YHeight = Height;
        SetColor(DiscColor);

        var c = new CircleShape2D();
        c.Radius = Diameter / 2f;
        ShapeProfiles.Add(new OffsetShape2D(c));

        return true;
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Height, Diameter);

    private float Height;
    private float Diameter;
    private Color DiscColor;
}

public sealed class DiscParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Disc;

    public float Height { get; set; }
    public float Diameter { get; set; }
    public Color Color { get; set; } = Colors.Black;
}

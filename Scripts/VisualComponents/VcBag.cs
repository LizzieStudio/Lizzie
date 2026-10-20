using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

public partial class VcBag : VisualComponentGroup
{
    private Label3D _componentCount;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Bag;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        _componentCount = GetNode<Label3D>("ComponentCount");
        DragDropCollider = GetNode<CollisionShape3D>("DrawCollider");
        CanAcceptDrop = true;
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        AtomService.Instance.Watch(this, SyncCount);
    }

    private void SyncCount(IWatchReader R) =>
        _componentCount.Text = R.Count(ComponentPosition.ByContainer, Key).ToString();

    private IEnumerable<ComponentPosition> Contents(IAtomReader R) =>
        R.Get(ComponentPosition.ByContainer, Key);

    protected override void Setup(ComponentParameters parameters, IAtomReader R)
    {
        base.Setup(parameters, R);
        var p = (BagParameters)parameters;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");

        if (p.Height <= 0)
            return;

        Height = p.Height / 10f;
        Diameter = p.Diameter / 10f;
        BagColor = p.Color;

        //create cube
        if (Diameter <= 0)
        {
            Scale = new Vector3(Height, Height, Height);
        }
        else
        {
            Scale = new Vector3(Diameter, Height, Diameter);
        }

        YHeight = Height * 2;

        SetColor(BagColor);
        _componentCount = GetNode<Label3D>("ComponentCount");
        _componentCount.Visible = p.ShowCount;

        var c = new CircleShape2D();
        c.Radius = Diameter / 2;

        ShapeProfiles.Add(new OffsetShape2D(c));
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Height, Diameter);

    private float Height;
    private float Diameter;
    private Color BagColor;

    public override (ComponentPosition[], Atom[]) DragDraw()
    {
        var contents = Contents(AtomService.Instance).ToList();
        return contents.Count == 0
            ? ([], [])
            : ([contents[Random.Shared.Next(contents.Count)] with { Position = Vector3.Zero }], []);
    }
}

[JsonName("BagParameters")]
public sealed record BagParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Bag;

    public float Height { get; init; }
    public float Diameter { get; init; }
    public Color Color { get; init; } = Colors.Black;
    public bool ShowCount { get; init; }
}

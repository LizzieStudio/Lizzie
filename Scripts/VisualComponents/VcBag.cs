using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

public partial class VcBag : VisualComponentGroup
{
    private VisualComponentBase _contents;
    private string _contentsDataRow;
    private Label3D _componentCount;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Bag;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        _componentCount = GetNode<Label3D>("ComponentCount");
        DragDropCollider = GetNode<CollisionShape3D>("DrawCollider");
        UpdateComponentCount();
        CanAcceptDrop = true;
    }

    private void UpdateComponentCount()
    {
        _componentCount.Text = Children.Count().ToString();
    }


    protected override bool Setup(ComponentParameters parameters, IRecordReader R)
    {
        base.Setup(parameters, R);
        var p = (BagParameters)parameters;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        if (p.Height <= 0)
            return false;

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

        return true;
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Height, Diameter);

    private float Height;
    private float Diameter;
    private Color BagColor;

    private Vector3 _lastScale;

    private void UpdateChildScale(Node3D c)
    {
        if (Math.Abs(c.Scale.Length() - _lastScale.Length()) >= 0.05f)
        {
            var s = c.Scale;
            _lastScale = new Vector3(s.X / Diameter, s.Y / Height, s.Z / Diameter);
            c.Scale = _lastScale;
        }
    }

    protected override void OnChildrenChanged()
    {
        UpdateComponentCount();
    }

    public override TableEvent DragDraw(int quantity)
    {
        return ProjectService.Instance.GameObjects.BuildDrawEvent(DrawRandom(quantity));
    }
}

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

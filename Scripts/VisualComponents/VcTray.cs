using System;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

namespace Lizzie;

public partial class VcTray : VisualComponentGroup
{
    private Prototype _prototype;

    // What the tray shows and hands out: its prototype's first item, such as a printed prototype's first card.
    private PrototypeItemRef _item;
    private Node3D _prototypeSpawnPoint;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Tray;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        _prototypeSpawnPoint = GetNode<Node3D>("ProtoAnchor");
        DragDropCollider = GetNode<CollisionShape3D>("DrawCollider");
    }

    public override void _Process(double delta)
    {
        foreach (var c in _prototypeSpawnPoint.GetChildren())
        {
            if (c is Node3D n)
            {
                n.Rotate(Vector3.Up, (float)delta);
                //n.Rotate(Vector3.Right, (float)(delta * 1.3));
            }
        }
    }

    protected override void Setup(ComponentParameters parameters, IAtomReader R)
    {
        base.Setup(parameters, R);
        Apply((TrayParameters)parameters, R);
    }

    /// <summary>Sizes the tray and shows the prototype it hands out.</summary>
    private void Apply(TrayParameters p, IAtomReader R)
    {
        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");

        if (p.Height <= 0)
            return;

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

        _prototypeSpawnPoint.Scale = Vector3.One / Scale;

        YHeight = Height * 2;

        SetColor(CubeColor);

        var r = new RectangleShape2D();
        r.Size = new Vector2(Width, Length);

        ShapeProfiles.Add(new OffsetShape2D(r));

        _prototype = R.Get(Prototype.ById, p.DispenseRef);
        _item =
            _prototype == null
                ? null
                : PrototypeItemRef.All(_prototype.Parameters, R).FirstOrDefault();

        UpdateNameLabel();
        CreateTrayPrototype();
    }

    public override GeometryInstance3D DragMesh => MainMesh;

    public override float MaxAxisSize => Math.Max(Math.Max(Height, Width), Length);

    private float Height;
    private float Width;
    private float Length;
    private Color CubeColor;

    private Label3D _nameLabel;

    private void UpdateNameLabel()
    {
        if (_nameLabel == null)
        {
            _nameLabel = GetNode<Label3D>("ComponentName");
            _nameLabel.Name = "NameLabel";
        }

        _nameLabel.Text = _prototype?.Name;

        // Position just above the top face of the tray
        //_nameLabel.Position = new Vector3(0, Height + 0.01f, 0);
    }

    private void CreateTrayPrototype()
    {
        foreach (var child in _prototypeSpawnPoint.GetChildren())
            child.QueueFree();

        if (_prototype == null)
            return;

        var c = ProjectService.Instance.SpawnDisconnectedVisualComponent(
            _prototype,
            _item,
            TextureFactory
        );
        c.Built += () => c.Position = new Vector3(0, c.YHeight, 0);
        _prototypeSpawnPoint.AddChild(c);
    }

    public override (ComponentPosition[], Atom[]) DragDraw()
    {
        if (_prototype == null)
            return ([], []);
        var id = Ref<Component>.Create();
        return (
            [new ComponentPosition { Id = id, StageRef = Key.StageRef }],
            [
                new Component
                {
                    Id = id,
                    PrototypeRef = _prototype.Id,
                    Item = _item,
                },
            ]
        );
    }
}

[JsonName("TrayParameters")]
public sealed record TrayParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Tray;

    public float Height { get; init; }
    public float Width { get; init; }
    public float Length { get; init; }
    public Color Color { get; init; } = Colors.Black;

    public Ref<Prototype> DispenseRef { get; init; }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;

public partial class VcTray : VisualComponentGroup
{
    private Prototype _prototype;
    private VisualComponentBase _contents;
    private string _contentsDataRow;
    private Node3D _prototypeSpawnPoint;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Tray;

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        _prototypeSpawnPoint = GetNode<Node3D>("ProtoAnchor");
        DragDropCollider = GetNode<CollisionShape3D>("DrawCollider");
    }

    public override void _Process(double delta)
    {
        if (_trayProtoBuildNeeded)
        {
            CreateTrayPrototype(TextureFactory);
        }

        foreach (var c in _prototypeSpawnPoint.GetChildren())
        {
            if (c is Node3D n)
            {
                UpdateChildScale(n);
                n.Rotate(Vector3.Up, (float)delta);
                //n.Rotate(Vector3.Right, (float)(delta * 1.3));
            }
        }
    }

    public override bool Setup(ComponentParameters parameters, TextureFactory textureFactory)
    {
        base.Setup(parameters, textureFactory);

        return Apply((TrayParameters)parameters, ProjectService.Instance);
    }

    protected override void Sync(IRecordReader R)
    {
        var proto = R.Get<Prototype>(PrototypeRef);
        if (proto == null || TextureFactory == null)
            return;

        base.Setup(proto.Parameters, TextureFactory);
        Apply((TrayParameters)proto.Parameters, R);
    }

    /// <summary>Sizes the tray and shows the prototype it hands out.</summary>
    private bool Apply(TrayParameters p, IRecordReader R)
    {
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

        YHeight = Height * 2;

        SetColor(CubeColor);

        var r = new RectangleShape2D();
        r.Size = new Vector2(Width, Length);

        ShapeProfiles.Add(new OffsetShape2D(r));

        _prototype = R.Get<Prototype>(p.Prototype);

        UpdateNameLabel();
        CreateTrayPrototype(TextureFactory);

        return true;
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

    private bool _trayProtoBuildNeeded;

    private void CreateTrayPrototype(TextureFactory textureFactory)
    {
        if (!IsNodeReady())
        {
            _trayProtoBuildNeeded = true;
            return;
        }

        _trayProtoBuildNeeded = false;

        foreach (var child in _prototypeSpawnPoint.GetChildren())
            child.QueueFree();

        if (_prototype == null)
            return;

        var c = ProjectService.Instance.SpawnDisconnectedVisualComponent(
            _prototype,
            textureFactory
        );
        UpdateChildScale(c);
        _prototypeSpawnPoint.AddChild(c);
        c.Position += new Vector3(0, c.YHeight, 0);
    }

    private Vector3 _lastScale;

    private void UpdateChildScale(Node3D c)
    {
        if (Math.Abs(c.Scale.Length() - _lastScale.Length()) >= 0.05f)
        {
            var s = c.Scale;
            _lastScale = new Vector3(s.X / Width, s.Y / Height, s.Z / Length);
            c.Scale = _lastScale;
        }
    }

    protected override void OnChildrenChanged() { }

    public override TableEvent DragDraw(int quantity)
    {
        if (_prototype == null)
            return null;

        if (PresenceSynchronizer.Instance is not { } cursors)
            return null;

        var id = Snowport.Clock.CreateTag();

        return TableEvent.Now(
            null,
            new ComponentEffect
            {
                Id = id,
                PrototypeRef = _prototype.Id,
                State = new VcSyncDto
                {
                    Location = ComponentLocation.Cursor,
                    ContainerRef = cursors.LocalCursorRef,
                },
            }
        );
    }
}

public sealed record TrayParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Tray;

    public float Height { get; init; }
    public float Width { get; init; }
    public float Length { get; init; }
    public Color Color { get; init; } = Colors.Black;

    public SnowTag Prototype { get; init; } = SnowTag.Empty;
}

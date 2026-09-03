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
            CreateTrayPrototype(_textureFactory);
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

    private TextureFactory _textureFactory;

    public override bool Setup(
        ComponentParameters parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        _textureFactory = textureFactory;

        base.Setup(parameters, dataSetRow, textureFactory);
        var p = (TrayParameters)parameters;

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

        if (Guid.TryParse(p.Prototype, out var gKey))
        {
            ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(gKey, out _prototype);
        }

        UpdateNameLabel();
        CreateTrayPrototype(textureFactory);

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
            string.Empty,
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

    public override void DragDraw(int quantity)
    {
        if (_prototype == null)
            return;

        var id = Snowport.Clock.Create();

        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(
                null,
                new CreateEffect
                {
                    ComponentRef = id,
                    PrototypeRef = _prototype.PrototypeRef,
                    ComponentName = _prototype.Name ?? string.Empty,
                    State = new VcSyncDto { Location = ComponentLocation.Board },
                }
            )
        );

        ProjectService.Instance.GameObjects.ShowAndDrag(new List<SnowportId> { id });
    }
}

public sealed class TrayParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Tray;

    public float Height { get; set; }
    public float Width { get; set; }
    public float Length { get; set; }
    public Color Color { get; set; } = Colors.Black;

    public string Prototype { get; set; } = "";
}

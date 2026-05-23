using System;
using System.Collections.Generic;
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

    public override bool Setup(
        Dictionary<string, object> parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        return Setup(parameters, textureFactory);
    }

    private TextureFactory _textureFactory;

    public override bool Setup(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        _textureFactory = textureFactory;

        base.Setup(parameters, string.Empty, textureFactory);

        MainMesh = GetNode<GeometryInstance3D>("ObjectMesh");
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");

        if (parameters.ContainsKey(nameof(Height)))
        {
            var h = Utility.GetParam<float>(parameters, "Height");
            if (h <= 0)
                return false;
            Height = h / 10f;

            var w = Utility.GetParam<float>(parameters, "Width");
            Width = w / 10f;

            var l = Utility.GetParam<float>(parameters, "Length");
            Length = l / 10f;

            if (parameters["Color"] is Color color)
            {
                CubeColor = color;
            }
        }

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

        var pKey = Utility.GetParam<string>(parameters, "Prototype");
        if (Guid.TryParse(pKey, out var gKey))
        {
            ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(gKey, out _prototype);
        }

        UpdateNameLabel();
        CreateTrayPrototype(textureFactory);

        return true;
    }

    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
    {
        var ret = new List<string>();

        //must have a name and height. Width/length optional
        if (parameters.ContainsKey(nameof(ComponentName)))
        {
            if (string.IsNullOrEmpty(parameters[nameof(ComponentName)].ToString()))
                ret.Add("Instance Name may not be blank");
        }
        else
        {
            ret.Add("Instance Name not included");
        }

        if (parameters.ContainsKey(nameof(Height)))
        {
            var h = Utility.GetParam<float>(parameters, "Height");
            if (h <= 0)
                ret.Add("Height must be > 0");
        }
        else
        {
            ret.Add("Height not included");
        }

        return ret;
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
        EventBus.Instance.Publish(
            new SpawnPrototypeEvent
            {
                PrototypeRef = _prototype.PrototypeRef,
                StartInDragMode = true,
            }
        );
    }
}

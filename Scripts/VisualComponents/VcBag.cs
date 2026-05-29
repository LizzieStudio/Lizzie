using System;
using System.Collections.Generic;
using System.Linq;
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

            var d = Utility.GetParam<float>(parameters, "Diameter");
            Diameter = d / 10f;

            if (parameters["Color"] is Color color)
            {
                BagColor = color;
            }
        }

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
        var _showCount = Utility.GetParam<bool>(parameters, "ShowCount");
        _componentCount.Visible = _showCount;

        var c = new CircleShape2D();
        c.Radius = Diameter / 2;

        ShapeProfiles.Add(new OffsetShape2D(c));

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

    public override void DragDraw(int quantity)
    {
        var gList = DrawRandom(quantity).ToList();
        if (!gList.Any())
            return;

        EventBus.Instance.Publish(new ShowAndDragComponentEvent { ComponentList = gList });
    }
}

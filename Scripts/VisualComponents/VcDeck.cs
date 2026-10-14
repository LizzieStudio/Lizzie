using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

public partial class VcDeck : VisualComponentBase
{
    private Sprite3D _outline;
    private MeshInstance3D _outlineShape;
    private Label3D _componentCount;
    private Label3D _blankLabel;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Deck;

        _outlineShape = GetNode<MeshInstance3D>("OutlineShape");
        _outlineShape.Layers = 0;
        _outline = GetNode<Sprite3D>("Outline");
        _componentCount = GetNode<Label3D>("ComponentCount");
        _blankLabel = GetNode<Label3D>("%BlankLabel");
    }

    public override GeometryInstance3D DragMesh => _outline;

    // The deck draws only a frame sprite, so the outline traces a box added specifically for it.
    protected override IEnumerable<MeshInstance3D> OutlineMeshes() => [_outlineShape];

    public override float MaxAxisSize => Math.Max(_height, _width);

    public override void _EnterTree()
    {
        base._EnterTree();
        RecordService.Instance.Watch(this, SyncCount);
    }

    private void SyncCount(IRecordReader R)
    {
        if (R.Get<ComponentState>(Reference) is { } deck && !R.IsBeingDragged(deck))
            _componentCount.Text = R.TokensOn(deck).Count.ToString();
    }

    /// <summary>
    /// The deck's cards, face-down on the frame, with the first token on top.
    /// </summary>
    public override IEnumerable<ComponentState> GetSpawnStack(ComponentState self)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return [];
        var proto = RecordService.Instance.GetIncludingDeleted<Prototype>(PrototypeRef);
        if (proto?.Parameters is not PrintedParameters parameters)
            return [];

        var R = RecordService.Instance;
        return Enumerable
            .Range(0, parameters.CardCount(R))
            .Reverse()
            .Select(n => new ComponentState
            {
                Id = Snowport.Clock.CreateTag(),
                PrototypeRef = PrototypeRef,
                Card = parameters.Card(n, R),
                X = self.X,
                Z = self.Z,
                Rotation = new Vector3(0, self.Rotation.Y, Mathf.Pi),
            })
            .ToList();
    }

    protected override void Setup(ComponentParameters parameters, IRecordReader R)
    {
        base.Setup(parameters, R);

        _blankLabel = GetNode<Label3D>("%BlankLabel");

        var p = (PrintedParameters)parameters;
        if (p.Height <= 0)
            return;

        _height = p.Height / 10f;
        _width = p.Width / 10f;
        _blankLabel.Text = ComponentName;

        YHeight = Thickness + 0.03f;
        Scale = new Vector3(_width, Thickness, _height);

        ShapeProfiles.Add(VcToken.ShapeProfile((TokenShape)p.Shape, _width, _height));

        TextureReady = true;
    }

    private float _height;
    private float _width;

    private const float Thickness = 0.15f;
}

[JsonName("DeckParameters")]
public sealed record DeckParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Deck;
}

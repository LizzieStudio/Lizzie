using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class VcDeck : VisualComponentBase
{
    // Holds the frame's parts, scaled to the deck's size. The cards are children of the deck itself, so they don't inherit it.
    private Node3D _frame;

    // The collider for pressing the frame. A collider only works as a direct child of the deck,
    // so it isn't in the frame, and is placed as if it were.
    private CollisionShape3D _patch;
    private Transform3D _patchInFrame;
    private Sprite3D _outline;
    private MeshInstance3D _outlineShape;
    private Label3D _componentCount;
    private Label3D _blankLabel;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Deck;

        _frame = GetNode<Node3D>("Frame");
        _patch = GetNode<CollisionShape3D>("CollisionShape3D");
        _patchInFrame = _patch.Transform;
        _outlineShape = GetNode<MeshInstance3D>("Frame/OutlineShape");
        _outlineShape.Layers = 0;
        _outline = GetNode<Sprite3D>("Frame/Outline");
        _componentCount = GetNode<Label3D>("Frame/ComponentCount");
        _blankLabel = GetNode<Label3D>("%BlankLabel");
    }

    public override GeometryInstance3D DragMesh => _outline;

    // The deck draws only a frame sprite, so the outline traces a box added specifically for it.
    protected override IEnumerable<MeshInstance3D> OutlineMeshes() => [_outlineShape];

    public override float MaxAxisSize => Math.Max(_height, _width);

    public override void _EnterTree()
    {
        base._EnterTree();
        AtomService.Instance.Watch(this, SyncCount);
        AtomService.Instance.Watch(this, SyncLayout);
    }

    private void SyncCount(IWatchReader R)
    {
        if (R.Get(ComponentPosition.ById, Key) is { } deck && !R.IsBeingDragged(deck))
            _componentCount.Text = R.Count(ComponentPosition.ByContainer, Key).ToString();
    }

    /// <summary>
    /// Works out the internal layout and external size.
    /// </summary>
    private void SyncLayout(IWatchReader R)
    {
        var cards = R.Get(ComponentPosition.ByContainer, Key).OrderBy(c => c.ZOrder).ToList();
        var heights = cards.Select(c => CardHeight(R, c)).ToList();
        float total = FrameHeight + heights.Sum();

        var nodes = GetChildren().OfType<VisualComponentBase>().ToDictionary(n => n.Reference);
        float top = FrameHeight;
        for (int i = 0; i < cards.Count; i++)
        {
            if (nodes.TryGetValue(cards[i].Id, out var node))
                node.MoveToTargetY(top + heights[i] / 2f - total / 2f);
            top += heights[i];
        }

        _frame.Position = new Vector3(0, (FrameHeight - total) / 2f, 0);
        _patch.Transform = _frame.Transform * _patchInFrame;
        YHeight = total;
        OwningView?.NotifyChanged();
    }

    private static float CardHeight(IAtomReader R, ComponentPosition card) =>
        R.GetPrototype(card)?.Parameters is PrintedParameters p ? VcPrinted.ThicknessOf(p) : 0;

    /// <summary>
    /// The deck's cards, face-down on the frame, with the first card on top.
    /// </summary>
    public override IEnumerable<Atom> GetSpawnStack(ComponentPosition self)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return [];
        var proto = AtomService.Instance.Get(Prototype.ById, PrototypeRef);
        if (proto?.Parameters is not PrintedParameters parameters)
            return [];

        var R = AtomService.Instance;
        return Enumerable
            .Range(0, parameters.CardCount(R))
            .Reverse()
            .SelectMany(n =>
            {
                var variant = new ComponentVariant
                {
                    Id = SnowTag.Create(),
                    PrototypeRef = PrototypeRef,
                    Item = parameters.Item(n, R),
                };
                return new Atom[]
                {
                    variant,
                    new ComponentPosition
                    {
                        Id = SnowTag.Create(),
                        Stage = self.Stage,
                        VariantRef = variant.Id,
                        ContainerRef = self.Id,
                        Rotation = new Vector3(0, 0, Mathf.Pi),
                    },
                };
            })
            .ToList();
    }

    protected override void Setup(ComponentParameters parameters, IAtomReader R)
    {
        base.Setup(parameters, R);

        _blankLabel = GetNode<Label3D>("%BlankLabel");

        var p = (PrintedParameters)parameters;
        if (p.Height <= 0)
            return;

        _height = p.Height / 10f;
        _width = p.Width / 10f;
        _blankLabel.Text = ComponentName;

        _frame.Scale = new Vector3(_width, Thickness, _height);
        _patch.Transform = _frame.Transform * _patchInFrame;

        ShapeProfiles.Add(VcPrinted.ShapeProfile((TokenShape)p.Shape, _width, _height));
    }

    private float _height;
    private float _width;

    private const float Thickness = 0.15f;

    private const float FrameHeight = Thickness + 0.03f;
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public abstract partial class VisualComponentBase : Area3D
{
    public enum VisualComponentType
    {
        Unset = 0,
        Cube = 1,
        Disc = 2,
        Token = 3,
        Deck = 6,
        Die = 7,
        Mesh = 8,
        Meeple = 9,
        Tray = 10,
        Bag = 11,
        Zone = 12,
    }

    public virtual VisualComponentType ComponentType { get; set; }
    protected GeometryInstance3D MainMesh;

    /// <summary>
    /// The component's Rotation.
    /// </summary>
    public new virtual Vector3 Rotation
    {
        get => base.Rotation;
        set => base.Rotation = value;
    }

    public virtual List<OffsetShape2D> ShapeProfiles { get; set; } = new();

    public override void _EnterTree()
    {
        base._EnterTree();

        // Normal nodes never change, since the Table replaces the node when they do.
        // Previews have no record and keep their own.
        var record = RecordService.Instance.Get(ComponentState.ById, Reference);
        if (record != null)
        {
            PrototypeRef = record.PrototypeRef;
            Card = record.Card;
        }

        // Builds first, so the first placement knows the component's height.
        RecordService.Instance.Watch(this, Sync);
        RecordService.Instance.Watch(this, SyncState);
        RecordService.Instance.Watch(this, SyncSelection);
    }

    /// <summary>Shows which players have this component selected.</summary>
    private void SyncSelection(IWatchReader R)
    {
        var target = new RecordTarget(Reference);
        var local = Snowport.Clock.source;
        var selection = R.GetSelection()?.Targets;
        _isMine = selection?.Contains(target) == true;
        _localColor = R.SeatColor(local);
        _nothingSelected = selection?.IsEmpty != false;
        var other = R.Get(Selection.ByWithin, SnowTag.Empty)
            .Where(s => s.Player != local && s.Targets.Contains(target))
            .MaxBy(R.Version);
        _othersColor = other == null ? null : R.SeatColor(other.Player);
        UpdateHighlight();
    }

    /// <summary>
    /// Raised after each Sync that builds the component.
    /// </summary>
    public event Action Built;

    /// <summary>
    /// An unsaved prototype shown instead of <see cref="PrototypeRef"/>.
    /// Used for the editor preview, since there is no protoype record yet.
    /// </summary>
    public Prototype DraftPrototype
    {
        get;
        set
        {
            field = value;
            RecordService.Instance.QueueSync(this);
        }
    }

    /// <summary>The prototype this component shows. Possibly a draft or even deleted.</summary>
    protected Prototype GetPrototype(IRecordReader R) =>
        DraftPrototype ?? R.Get(Prototype.ById, PrototypeRef);

    private Tween _yTween;
    private float _targetY;

    public void MoveToTargetY(float y)
    {
        bool tweening = IsInstanceValid(_yTween) && _yTween.IsRunning();
        if (Mathf.IsEqualApprox(tweening ? _targetY : Position.Y, y))
            return;

        if (IsInstanceValid(_yTween))
            _yTween.Kill();
        _targetY = y;
        _yTween = GetTree().CreateTween();
        _yTween.TweenProperty(this, "position:y", y, 0.2f);
    }

    /// <summary>
    /// Builds the component from its prototype's parameters.
    /// </summary>
    protected virtual void Setup(ComponentParameters parameters, IRecordReader R)
    {
        ShapeProfiles.Clear();

        if (parameters != null && !string.IsNullOrEmpty(parameters.ComponentName))
            ComponentName = parameters.ComponentName;
    }

    private void Sync(IWatchReader R)
    {
        var proto = GetPrototype(R);
        if (proto == null || TextureFactory == null)
            return;

        Setup(proto.Parameters, R);
        RebuildOutline();
        Built?.Invoke();

        // A rebuild can change the component's size, so the table restacks.
        GetParentOrNull<Table>()?.NotifyChanged();
    }

    // whether SyncState has placed the node yet
    private bool _placed;

    /// <summary>
    /// Applies the component's record, animated by the command that wrote it if the write is recent enough.
    /// </summary>
    private void SyncState(IWatchReader R)
    {
        var s = R.Get(ComponentState.ById, Reference);
        if (s == null)
            return;

        var held = R.IsBeingDragged(s);
        LogicalVisible = s.IsOnTable || held && !R.IsHeldOverHand(s);

        // A new state ends any slide toward the old one.
        if (IsInstanceValid(_slide))
            _slide.Kill();

        // Only a recent write animates. Anything older jumps into place, to catch up.
        long age = Snowport.Clock.MsecSince(R.Version(s));
        var cutoff = ProjectService.Instance.GameObjects.AnimationCutoff;
        var writtenBy = _placed && age <= cutoff ? R.WrittenBy(s) : null;

        // A held node is placed by its holder's cursor.
        if (!held)
        {
            var target = s.PositionAt(_placed ? Position.Y : YHeight / 2f);
            if (writtenBy != DragCommands.Drop || !Slide(target, age))
                Position = target;
        }
        if (!PlayTransition(s, writtenBy, age))
            Rotation = s.Rotation;
        _placed = true;

        GetParentOrNull<Table>()?.NotifyChanged();
    }

    private Tween _slide;

    /// <summary>
    /// Slides across the table to <paramref name="target"/> at <see cref="GameObjects.DropSpeed"/>,
    /// minus the <paramref name="msecSinceStart"/> that's already gone by.
    /// </summary>
    /// <returns>true when it still has movement to go</returns>
    private bool Slide(Vector3 target, long msecSinceStart)
    {
        var from = new Vector2(Position.X, Position.Z);
        var to = new Vector2(target.X, target.Z);
        var speed = GetParentOrNull<Table>()?.GetParentOrNull<GameObjects>()?.DropSpeed ?? 0;
        if (speed <= 0)
            return false;
        float remaining = from.DistanceTo(to) / speed - msecSinceStart / 1000f;
        if (remaining <= 0)
            return false;

        _slide = CreateTween().SetParallel();
        _slide.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _slide.TweenProperty(this, "position:x", target.X, remaining);
        _slide.TweenProperty(this, "position:z", target.Z, remaining);
        return true;
    }

    /// <summary>
    /// The components to spawn as a stack on top of this one, bottom first.
    /// <strong>Their ZOrders do not come assigned.</strong>
    /// </summary>
    /// <param name="self">The state this component spawns with.</param>
    public virtual IEnumerable<ComponentState> GetSpawnStack(ComponentState self)
    {
        yield break;
    }

    /// <summary>
    /// Animates toward <paramref name="state"/> as <paramref name="writtenBy"/>, the command that wrote it, calls for.
    /// <paramref name="MsecSinceStart"/> is how many milliseconds ago the write was made.
    /// <paramref name="writtenBy"/> is null for a write too old to animate (see <see cref="GameObjects.AnimationCutoff"/>).
    /// Returns false when there's nothing to animate, so the state is applied immediately.
    /// </summary>
    public virtual bool PlayTransition(
        ComponentState state,
        CommandName? writtenBy,
        long MsecSinceStart
    ) => false;

    public TextureFactory TextureFactory { get; set; }

    public virtual string ComponentName { get; set; }

    public virtual SnowTag PrototypeRef { get; set; }

    private SnowTag _reference = Snowport.Clock.CreateTag();

    /// <summary>
    /// The component record this node shows. Must be set before adding the node to the tree.
    /// </summary>
    public SnowTag Reference
    {
        get => _reference;
        set
        {
            if (IsInsideTree())
                throw new InvalidOperationException("Reference can't change once in the tree.");
            _reference = value;
        }
    }

    /// <summary>
    /// The component's record, or null for a node without one, such as a spawn preview.
    /// Also null between its record being unset and the table removing it at the end of the frame.
    /// </summary>
    public ComponentState State => RecordService.Instance.Get(ComponentState.ById, Reference);

    /// <summary>
    /// Which of its prototype's cards this is, or <see cref="PrototypeItemRef.None"/>.
    /// </summary>
    public PrototypeItemRef Card { get; set; } = PrototypeItemRef.None;

    protected float _yHeight;

    public virtual float YHeight
    {
        get
        {
            if (Visible)
                return _yHeight;
            return 0;
        }
        protected set => _yHeight = value;
    }

    /// <summary>
    /// The set of Shape3Ds that define the collision volume. Will be a single Shape3D for most items.
    /// </summary>
    public virtual Shape3D[] Bounds { get; protected set; }

    private bool _isDrawSelected;

    /// <summary>
    /// Whether the mouse is over a container's draw area, so a drag draws from it.
    /// </summary>
    public bool IsDrawSelected
    {
        get => _isDrawSelected;
        set
        {
            _isDrawSelected = value;
            UpdateHighlight();
        }
    }

    private bool _isHovered;

    /// <summary>
    /// Whether the mouse is over the component.
    /// </summary>
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered == value)
                return;

            _isHovered = value;

            UpdateHighlight();
        }
    }

    // whether the local player has this component selected
    private bool _isMine;

    // the local player's seat color, which their selection shows in
    private Color _localColor;

    // whether the local player has nothing selected on the table, so keys act on the hovered component
    private bool _nothingSelected = true;

    // the color of the other player who most recently selected this component, or null
    private Color? _othersColor;

    private bool? _previewSelected;

    /// <summary>
    /// Whether a box selection in progress will select this component.
    /// Shown in place of the selection until the box is released. Null when not boxing.
    /// </summary>
    public bool? PreviewSelected
    {
        get => _previewSelected;
        set
        {
            if (_previewSelected == value)
                return;

            _previewSelected = value;

            UpdateHighlight();
        }
    }

    private bool _isDropTarget;

    /// <summary>
    /// Whether what the local player holds would drop into this component.
    /// </summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
                return;

            _isDropTarget = value;

            UpdateHighlight();
        }
    }

    // The outline of a drop target.
    private static readonly Color DropTargetColor = Colors.Yellow;

    // The outline over a container's draw area.
    private static readonly Color DrawAreaColor = Colors.CornflowerBlue;

    // The outline of a plain hover.
    private static readonly Color PlainHoverColor = Colors.White;

    /// <summary>
    /// Outlines the component when it's a drop target, hovered, or selected by anyone.
    /// </summary>
    protected virtual void UpdateHighlight()
    {
        var mine = PreviewSelected ?? _isMine;
        Color? hover =
            IsDrawSelected ? DrawAreaColor
            : _nothingSelected ? PlainHoverColor
            : null;
        SetOutline(
            NeverHighlight ? null
            : IsDropTarget ? DropTargetColor
            : IsHovered && hover != null ? hover
            : mine ? _localColor
            : _othersColor
        );
    }

    // Flat copies of the component's meshes that draw it into the outline mask.
    private readonly List<MeshInstance3D> _outlineCopies = new();
    private Color? _outlineColor;

    // The color reserved with SelectionOutline.
    private Color? _reservedColor;

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
            ReleaseOutlineColor();
    }

    private void ReleaseOutlineColor()
    {
        if (_reservedColor is Color held)
            SelectionOutline.Release(held);
        _reservedColor = null;
    }

    private void SetOutline(Color? color)
    {
        if (color == _outlineColor && (color == null || _outlineCopies.Count > 0))
            return;

        _outlineColor = color;
        if (color is not Color c)
        {
            ClearOutlineCopies();
            return;
        }

        if (_outlineCopies.Count == 0)
            BuildOutlineCopies();

        ReleaseOutlineColor();
        var material = SelectionOutline.Reserve(c);
        _reservedColor = c;
        foreach (var copy in _outlineCopies)
            copy.MaterialOverride = material;
    }

    /// <summary>
    /// Remakes the outline copies after the component's meshes change.
    /// </summary>
    private void RebuildOutline()
    {
        ClearOutlineCopies();
        var color = _outlineColor;
        _outlineColor = null;
        SetOutline(color);
    }

    private void ClearOutlineCopies()
    {
        ReleaseOutlineColor();
        foreach (var copy in _outlineCopies)
        {
            if (!IsInstanceValid(copy))
                continue;
            copy.GetParent()?.RemoveChild(copy);
            copy.QueueFree();
        }
        _outlineCopies.Clear();
    }

    /// <summary>
    /// The meshes whose shape the selection outline traces.
    /// </summary>
    protected virtual IEnumerable<MeshInstance3D> OutlineMeshes()
    {
        var meshes = new List<MeshInstance3D>();
        CollectMeshes(this, meshes);
        return meshes;
    }

    private void BuildOutlineCopies()
    {
        // Each copy of a mesh is a child of that mesh.
        // That way if we animate the mesh, the outline moves with it.
        foreach (var mesh in OutlineMeshes())
        {
            var copy = new MeshInstance3D
            {
                Mesh = mesh.Mesh,
                Layers = SelectionOutline.MaskLayer,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            mesh.AddChild(copy, false, InternalMode.Back);
            _outlineCopies.Add(copy);
        }
    }

    private void CollectMeshes(Node node, List<MeshInstance3D> meshes)
    {
        foreach (var child in node.GetChildren())
        {
            if (
                child is MeshInstance3D { Mesh: not null } mesh
                && (mesh.Layers & SelectionOutline.MaskLayer) == 0
            )
                meshes.Add(mesh);
            CollectMeshes(child, meshes);
        }
    }

    public Aabb Aabb
    {
        get
        {
            if (MainMesh != null)
            {
                return MainMesh.GlobalTransform * MainMesh.GetAabb();
            }

            return new Aabb();
        }
    }

    public abstract float MaxAxisSize { get; }

    private bool _neverHighlight = false;

    public bool NeverHighlight
    {
        get => _neverHighlight;
        set
        {
            _neverHighlight = value;
            UpdateHighlight();
        }
    }

    public abstract GeometryInstance3D DragMesh { get; }

    public virtual bool CanAcceptDrop { get; set; } = false;

    public virtual void SetColor(Color color)
    {
        var objMesh = GetNode<MeshInstance3D>("ObjectMesh");
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = color;
        objMesh.MaterialOverride = mat;
    }

    /// <summary>
    /// Need to override this if this simplistic transparency method doesn't work
    /// </summary>
    /// <param name="enableDim"></param>
    public virtual void DimMode(bool enableDim)
    {
        if (DragMesh == null)
            return;

        if (enableDim)
        {
            DragMesh.Transparency = 0.5f;
        }
        else
        {
            DragMesh.Transparency = 0;
        }
    }

    /// <summary>
    /// Local-only. While dragged, the height this component rests at within its dragged group.
    /// </summary>
    public float DragFloor { get; set; }

    /// <summary>True while this component is being dragged by any player's cursor.</summary>
    public bool IsDragging => State is { } s && RecordService.Instance.IsBeingDragged(s);

    private bool _logicalVisible = true;

    /// <summary>
    /// Visibility of this component, but overriden by zones.
    /// </summary>
    public bool LogicalVisible
    {
        get => _logicalVisible;
        set
        {
            _logicalVisible = value;
            ApplyEffectiveVisibility();
        }
    }

    private bool _zoneHidden;

    /// <summary>
    /// Local-only, non-synced mask set by <see cref="ZoneService"/> when the local
    /// player is not permitted to see this component inside a hiding zone.
    /// </summary>
    public bool ZoneHidden
    {
        get => _zoneHidden;
        set
        {
            if (_zoneHidden == value)
                return;
            _zoneHidden = value;
            ApplyEffectiveVisibility();
        }
    }

    /// <summary>
    /// Local-only flag set by <see cref="ZoneService"/>. false when the local player may
    /// see but not move this component (or cannot see it at all). Blocks drag start.
    /// </summary>
    public bool LocallyMovable { get; set; } = true;

    private void ApplyEffectiveVisibility()
    {
        Visible = _logicalVisible && !_zoneHidden;
    }
}

public class OffsetShape2D(Shape2D shape, Vector2 offset)
{
    public OffsetShape2D(Shape2D shape)
        : this(shape, Vector2.Zero) { }

    public Shape2D Shape { get; set; } = shape;
    public Vector2 Offset { get; set; } = offset;
}

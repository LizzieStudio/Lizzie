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

    public bool TextureReady { get; set; }
    public bool TextureChanged { get; set; }

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

    public const int TooltipTime = 1000;
    private float _curScale = 1;

    public override void _EnterTree()
    {
        base._EnterTree();

        // Normal nodes never change, since the Table replaces the node when they do.
        // Previews have no record and keep their own.
        var record = RecordService.Instance.GetIncludingDeleted<ComponentState>(Reference);
        if (record != null)
        {
            PrototypeRef = record.PrototypeRef;
            DataSetRowIndex = record.DataSetRowIndex;
            DataSetRowId = record.DataSetRowId;
        }

        // Builds first, so the first placement knows the component's height.
        RecordService.Instance.Watch(this, Sync);
        RecordService.Instance.Watch(this, SyncState);
        RecordService.Instance.Watch(this, SyncSelection);
    }

    /// <summary>Shows which players have this component selected.</summary>
    private void SyncSelection(IRecordReader R)
    {
        var target = new RecordTarget(Reference);
        var local = Snowport.Clock.source;
        _isMine = R.GetSelection()?.Targets.Contains(target) == true;
        var others = R.SelectionColors(s => s.Player != local && s.Targets.Contains(target));
        _othersColor = others.TryGetValue(target, out var color) ? color : null;
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
        DraftPrototype ?? R.GetIncludingDeleted<Prototype>(PrototypeRef);

    public override void _Ready()
    {
        _curScale = 1;

        //MouseEntered += _on_mouse_entered;
        MouseExited += _on_mouse_exited;

        base._Ready();
    }

    public override void _InputEvent(
        Camera3D camera,
        InputEvent @event,
        Vector3 eventPosition,
        Vector3 normal,
        int shapeIdx
    )
    {
        if (@event is InputEventMouseMotion mouse && !IsHeldByLocal)
        {
            if (shapeIdx == 0)
            {
                HoverColor = null;
                CanDrag = true;
                IsDrawSelected = false;
            }
            else
            {
                HoverColor = Colors.CornflowerBlue;
                IsDrawSelected = true;
                CanDrag = false;
            }
            _on_mouse_entered();
        }
        base._InputEvent(camera, @event, eventPosition, normal, shapeIdx);
    }

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
        TextureReady = false;
        ShapeProfiles.Clear();

        if (parameters != null && !string.IsNullOrEmpty(parameters.ComponentName))
            ComponentName = parameters.ComponentName;
    }

    private void Sync(IRecordReader R)
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

    // whether the last record SyncState applied was held
    private bool _held;

    /// <summary>
    /// Applies the component's record, animated by the command that wrote it if the write is recent enough.
    /// </summary>
    private void SyncState(IRecordReader R)
    {
        var s = R.Get<ComponentState>(Reference);
        if (s == null)
            return;

        var held = s.IsHeld;
        LogicalVisible = s.Location is ComponentLocation.Table or ComponentLocation.Cursor;
        // The cursor may have left it while it was held, which doesn't unhover it.
        if (_held && !held)
        {
            IsHovered = false;
        }
        _held = held;

        // A held node is placed by its holder's cursor.
        if (!held)
            Position = s.PositionAt(_placed ? Position.Y : YHeight / 2f);
        if (!PlayTransition(s, R.WrittenBy(s), Snowport.Clock.MsecSince(s.LastUpdateId)))
            Rotation = s.Rotation;
        _placed = true;

        GetParentOrNull<Table>()?.NotifyChanged();
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
    /// </summary>
    public ComponentState State =>
        RecordService.Instance.GetIncludingDeleted<ComponentState>(Reference);

    /// <summary>
    /// Index for grid and quick deck cards.
    /// Defaults to <c>-1</c>
    /// </summary>
    public virtual int DataSetRowIndex { get; set; } = -1;

    /// <summary>
    /// The dataset row that supplies this card's templating data. Defaults to <see cref="SnowTag.Empty"/>.
    /// </summary>
    public virtual SnowTag DataSetRowId { get; set; } = SnowTag.Empty;

    public virtual Polygon2D YProjection { get; private set; }

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

    //Component is selected for drawing tokens, cards, etc. Relevant for containers.
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

    /// <summary>Whether the mouse is over the component, or it's the target of a drag.</summary>
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

    private Color? _hoverColor;

    /// <summary>
    /// The outline color while hovered, e.g. yellow over a drop target.
    /// Null for <see cref="LocalSelection.LocalColor"/>.
    /// </summary>
    protected Color? HoverColor
    {
        get => _hoverColor;
        set
        {
            _hoverColor = value;
            UpdateHighlight();
        }
    }

    /// <summary>
    /// Outlines the component when it's hovered or selected by anyone.
    /// Hover shows <see cref="HoverColor"/>, and selection the color <see cref="LocalSelection.SelectionColors"/> gives it.
    /// </summary>
    protected virtual void UpdateHighlight()
    {
        var mine = PreviewSelected ?? _isMine;
        SetOutline(
            NeverHighlight ? null
            : IsHovered ? HoverColor ?? LocalSelection.LocalColor
            : mine ? LocalSelection.LocalColor
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

    private void _on_mouse_entered()
    {
        IsHovered = true;
    }

    private void _on_mouse_exited()
    {
        if (!IsHeldByLocal)
        {
            IsHovered = false;
        }

        // reset in case we were a drag target
        HoverColor = null;
    }

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

    public bool CanDrag { get; set; } = true;

    public virtual bool CanAcceptDrop { get; set; } = false;

    public virtual bool DragOver(IEnumerable<VisualComponentBase> dragObjects)
    {
        if (CanObjectsBeDropped(dragObjects))
        {
            HoverColor = Colors.Yellow;
            return true;
        }

        return false;
    }

    public void DragOverExit()
    {
        HoverColor = null;
    }

    public virtual bool CanObjectsBeDropped(IEnumerable<VisualComponentBase> dragObjects)
    {
        return true;
    }

    /// <summary>
    /// The records that drop the given components onto this one, or none if the drop produces no change.
    /// </summary>
    public virtual IEnumerable<Replicated> DropObjects(
        IEnumerable<VisualComponentBase> dragObjects
    ) => [];

    public virtual string GetPreviewComponentScene() => string.Empty;

    public virtual void SetColor(Color color)
    {
        var objMesh = GetNode<MeshInstance3D>("ObjectMesh");
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = color;
        objMesh.MaterialOverride = mat;
    }

    protected ImageTexture LoadTexture(string filename)
    {
        var image = new Image();
        var err = image.Load(filename);
        GD.Print(err);

        if (err == Error.Ok)
        {
            var texture = new ImageTexture();
            texture.SetImage(image);
            return texture;
        }

        return new ImageTexture();
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

    public enum ComponentLocation
    {
        Table,
        Container,
        Hand,
        Cursor,
    }

    /// <summary>
    /// Local-only. While dragged, the height this component rests at within its dragged group.
    /// </summary>
    public float DragFloor { get; set; }

    /// <summary>True while this component is being dragged by any player's cursor.</summary>
    public bool IsDragging => State?.IsHeld == true;

    /// <summary>True while this component is being held by the local player's cursor.</summary>
    public bool IsHeldByLocal => State is { IsHeld: true } s && s.Holder == Snowport.Clock.source;

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

    public void ApplyEffectiveVisibility()
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

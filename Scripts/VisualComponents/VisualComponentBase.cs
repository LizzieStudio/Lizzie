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

    private MeshInstance3D _highlightMesh;

    public virtual List<OffsetShape2D> ShapeProfiles { get; set; } = new();

    protected MeshInstance3D HighlightMesh
    {
        get => _highlightMesh;
        set
        {
            _highlightMesh = value;
            if (_highlightMesh != null)
                UpdateHighlight();
        }
    }

    [Export]
    private float _highlightScale = 1.1f;

    public const int TooltipTime = 1000;
    private float _curScale = 1;

    public override void _EnterTree()
    {
        base._EnterTree();
        ProjectService.Instance.Watch(this, Sync);
    }

    /// <summary>
    /// Raised after each Sync that builds the component.
    /// </summary>
    public event Action Built;

    private Prototype _draftPrototype;

    /// <summary>
    /// An unsaved prototype shown instead of <see cref="PrototypeRef"/>.
    /// Used for the editor preview, since there is no protoype record yet.
    /// </summary>
    public Prototype DraftPrototype
    {
        get => _draftPrototype;
        set
        {
            _draftPrototype = value;
            ProjectService.Instance.ForceSync(this);
        }
    }

    /// <summary>The prototype this component shows. Possibly a draft or even deleted.</summary>
    protected Prototype GetPrototype(IRecordReader R) =>
        DraftPrototype ?? R.GetIncludingDeleted<Prototype>(PrototypeRef);

    public override void _Ready()
    {
        _curScale = 1;

        IsMouseSelected = false;

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
                SetHighlightColor(Colors.White);
                CanDrag = true;
                IsDrawSelected = false;
            }
            else
            {
                SetHighlightColor(Colors.CornflowerBlue);
                IsDrawSelected = true;
                CanDrag = false;
            }
            _on_mouse_entered();
        }
        base._InputEvent(camera, @event, eventPosition, normal, shapeIdx);
    }

    // bug prevention: two events in rapid succession can fight each other
    private Tween _yTween;

    public void MoveToTargetY(float y)
    {
        if (IsInstanceValid(_yTween))
            _yTween.Kill();
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
        Built?.Invoke();
    }

    /// <summary>
    /// Applies the spawned state and builds immediately.
    /// </summary>
    public virtual void SpawnBuild(ComponentState syncDto, TextureFactory textureFactory)
    {
        syncDto.ApplyToComponent(this);
        TextureFactory = textureFactory;
        ProjectService.Instance.SyncNow(this);
    }

    /// <summary>
    /// Produce the effects for any contained components.
    /// <param name="containerRef">the id of the container to these children</param>
    /// </summary>
    public virtual IEnumerable<ComponentEffect> GetSpawnChildEffects(SnowTag containerRef)
    {
        yield break;
    }

    /// <summary>
    /// Produce the effects to delete this component.
    /// </summary>
    public virtual IEnumerable<ComponentEffect> GetDespawnEffects()
    {
        yield return new ComponentEffect(ComponentState.Capture(this) with { Deleted = true });
    }

    /// <summary>
    /// Processes legacy events and returns effects for everything else.
    /// </summary>
    public virtual Effect[] ProcessCommand(VisualCommand command)
    {
        if (command == VisualCommand.Delete)
            return GetDespawnEffects().ToArray();

        if (command == VisualCommand.RotateCcw)
            return [BuildRotation(ProjectService.Instance.RotationStep)];

        if (command == VisualCommand.RotateCw)
            return [BuildRotation(-1 * ProjectService.Instance.RotationStep)];

        if (command == VisualCommand.Duplicate)
        {
            EventBus.Instance.Publish(
                new SpawnPrototypeEvent
                {
                    PrototypeRef = PrototypeRef,
                    DataSetRowIndex = DataSetRowIndex,
                    DataSetRowId = DataSetRowId,
                }
            );
            return [];
        }

        if (command == VisualCommand.Edit)
        {
            EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeId = PrototypeRef });
        }

        if (command == VisualCommand.MakeUnique)
        {
            EventBus.Instance.Publish(new MakePrototypeUniqueEvent { PrototypeId = PrototypeRef });
        }

        return [];
    }

    /// <summary>
    /// Override in subclasses that support quantity-based commands (e.g. Draw N, Deal N).
    /// The base implementation produces no effects.
    /// <paramref name="quantity"/> is Int32.MaxValue when the user chose "All".
    /// </summary>
    public virtual Effect[] ProcessCommandWithQuantity(VisualCommand command, int quantity) => [];

    /// <summary>
    /// Animates toward <paramref name="state"/> using the <see cref="ComponentState.Transition"/>.
    /// <paramref name="MsecSinceStart"/> is how many milliseconds ago the write was made.
    /// Returns false when there's nothing to animate, so the state is applied immediately.
    /// </summary>
    public virtual bool PlayTransition(ComponentState state, long MsecSinceStart) => false;

    public TextureFactory TextureFactory { get; set; }

    public virtual List<MenuCommand> GetMenuCommands()
    {
        var l = new List<MenuCommand>();

        l.Add(new MenuCommand(VisualCommand.RotateCw));
        l.Add(new MenuCommand(VisualCommand.RotateCcw));
        l.Add(new MenuCommand(VisualCommand.Delete));
        l.Add(new MenuCommand(VisualCommand.Duplicate));
        l.Add(new MenuCommand(VisualCommand.Edit));
        l.Add(new MenuCommand(VisualCommand.MakeUnique));
        return l;
    }

    public virtual string ComponentName { get; set; }

    public virtual SnowTag PrototypeRef { get; set; }

    public virtual SnowTag Reference { get; set; } = Snowport.Clock.CreateTag();

    /// <summary>
    /// The container that holds this component or
    /// <see cref="SnowTag.Empty"/>.
    /// </summary>
    public SnowTag ContainerRef { get; set; } = SnowTag.Empty;

    /// <summary>
    /// Index for grid and quick deck cards.
    /// Defaults to <c>-1</c>
    /// </summary>
    public virtual int DataSetRowIndex { get; set; } = -1;

    /// <summary>
    /// The dataset row that supplies this card's templating data. Defaults to <see cref="SnowTag.Empty"/>.
    /// </summary>
    public virtual SnowTag DataSetRowId { get; set; } = SnowTag.Empty;

    /// <summary>
    /// True when this instance is an individual card rather than a deck container.
    /// </summary>
    public bool IsCardInstance => DataSetRowIndex >= 0 || DataSetRowId != SnowTag.Empty;

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
    /// The component's stacking order.
    /// </summary>
    private ZOrder _zOrder = new(ZTarget.Top, 0, SnowportId.Empty);

    public virtual ZOrder ZOrder
    {
        get => _zOrder;
        set => _zOrder = value;
    }

    /// <summary>
    /// The set of Shape3Ds that define the collision volume. Will be a single Shape3D for most items.
    /// </summary>
    public virtual Shape3D[] Bounds { get; protected set; }

    private bool _isMouseSelected;

    public virtual bool IsMouseSelected
    {
        get => _isMouseSelected;
        set
        {
            if (_isMouseSelected == value)
                return;

            _isMouseSelected = value;

            UpdateHighlight();
        }
    }

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

    public virtual bool IsHovered { get; set; }

    private bool _isClickSelected;

    public virtual bool IsClickSelected
    {
        get => _isClickSelected;
        set
        {
            if (_isClickSelected == value)
                return;

            _isClickSelected = value;

            UpdateHighlight();
        }
    }

    public bool IsSelected => IsMouseSelected || IsClickSelected;

    protected virtual void UpdateHighlight()
    {
        if (HighlightMesh == null)
            return;

        HighlightMesh.Visible = IsSelected && !NeverHighlight;
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
        IsMouseSelected = true;
        IsHovered = true;
    }

    private void _on_mouse_exited()
    {
        if (!IsHeldByLocal)
        {
            IsMouseSelected = false;
            IsHovered = false;
        }

        //TODO only call this when necessary
        SetHighlightColor(Colors.White); //reset in case we were a drag target
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
            SetHighlightColor(Colors.Yellow);
            return true;
        }

        return false;
    }

    public void DragOverExit()
    {
        SetHighlightColor(Colors.White);
    }

    public virtual bool CanObjectsBeDropped(IEnumerable<VisualComponentBase> dragObjects)
    {
        return true;
    }

    /// <summary>
    /// Builds the event for dropping the given components onto this one, or null if the
    /// drop produces no change.
    /// </summary>
    public virtual TableEvent DropObjects(IEnumerable<VisualComponentBase> dragObjects) => null;

    public virtual string GetPreviewComponentScene() => string.Empty;

    public virtual void SetColor(Color color)
    {
        var objMesh = GetNode<MeshInstance3D>("ObjectMesh");
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = color;
        objMesh.MaterialOverride = mat;
    }

    public virtual void SetHighlightColor(Color color)
    {
        var mat = _highlightMesh.GetActiveMaterial(0);
        if (mat is ShaderMaterial sm)
        {
            sm.SetShaderParameter("outline_color", color);
        }
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

    private ComponentLocation _location;

    public ComponentLocation Location
    {
        get => _location;
        set
        {
            var leavingCursor =
                _location == ComponentLocation.Cursor && value != ComponentLocation.Cursor;
            _location = value;
            LogicalVisible = value is ComponentLocation.Table or ComponentLocation.Cursor;
            if (leavingCursor)
                IsMouseSelected = false;
        }
    }

    /// <summary>
    /// While this component is being dragged, the relative position to the cursor.
    /// </summary>
    public Vector3 CursorOffset { get; set; }

    /// <summary>True while this component is being dragged by any player's cursor.</summary>
    public bool IsDragging => Location == ComponentLocation.Cursor;

    /// <summary>True while this component is being held by the local player's cursor.</summary>
    public bool IsHeldByLocal =>
        IsDragging
        && PresenceSynchronizer.Instance is { } cursors
        && ContainerRef == cursors.LocalCursorRef;

    private ComponentEffect BuildRotation(float degreesAboutY) =>
        new(
            ComponentState.Capture(this) with
            {
                Rotation = Rotation + new Vector3(0, Mathf.DegToRad(degreesAboutY), 0),
            }
        );

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

    /// <summary>
    /// The delta from the cursor prosition for this object in spawn mode
    /// </summary>
    public Vector3 SpawnDelta { get; set; } = Vector3.Zero;
}

public class OffsetShape2D(Shape2D shape, Vector2 offset)
{
    public OffsetShape2D(Shape2D shape)
        : this(shape, Vector2.Zero) { }

    public Shape2D Shape { get; set; } = shape;
    public Vector2 Offset { get; set; } = offset;
}

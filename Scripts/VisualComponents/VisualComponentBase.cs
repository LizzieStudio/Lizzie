using System;
using System.Collections.Generic;
using Godot;

public abstract partial class VisualComponentBase : Area3D
{
    public enum VisualComponentType
    {
        Cube = 0,
        Disc = 1,
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
        if (@event is InputEventMouseMotion mouse && !IsDragging)
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

    public void MoveToTargetY(float y)
    {
        var tween = GetTree().CreateTween();

        var newPos = new Vector3(Position.X, y, Position.Z);
        tween.TweenProperty(this, "position", newPos, 0.2f);
    }

    public virtual bool Setup(ComponentParameters parameters, TextureFactory textureFactory)
    {
        return Setup(parameters, DataSetRow, textureFactory);
    }

    public virtual bool Setup(
        ComponentParameters parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        TextureFactory = textureFactory;
        TextureReady = false;

        if (parameters != null && !string.IsNullOrEmpty(parameters.ComponentName))
            ComponentName = parameters.ComponentName;

        if (!string.IsNullOrEmpty(dataSetRow))
            DataSetRow = dataSetRow;

        return true;
    }

    public virtual bool Setup(Guid prototypeRef, TextureFactory textureFactory)
    {
        return Setup(prototypeRef, string.Empty, textureFactory);
    }

    public virtual bool Setup(Guid prototypeRef, string dataSetRow, TextureFactory textureFactory)
    {
        TextureFactory = textureFactory;
        TextureReady = false;

        if (ProjectService.Instance.CurrentProject == null)
            return false;

        if (
            !ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(
                prototypeRef,
                out var proto
            )
        )
        {
            return false;
        }

        PrototypeRef = prototypeRef;
        if (!string.IsNullOrEmpty(dataSetRow))
            DataSetRow = dataSetRow;

        Setup(proto.Parameters, textureFactory);

        return true;
    }

    public virtual void Build() { }

    public virtual void SpawnBuild(
        Guid prototypeRef,
        VcSyncDto syncDto,
        TextureFactory textureFactory
    )
    {
        syncDto.ApplyToComponent(this);
        Setup(prototypeRef, syncDto.DataSetRow, textureFactory);
    }

    /// <summary>
    /// Produce the effects for any contained components.
    /// </summary>
    public virtual IEnumerable<CreateEffect> GetSpawnChildEffects()
    {
        yield break;
    }

    /// <summary>
    /// Updates the textures, size, etc, without recreating any child objects.
    /// </summary>
    /// <param name="parameters"></param>
    /// <param name="textureFactory"></param>
    /// <returns></returns>
    public virtual bool Refresh(TextureFactory textureFactory)
    {
        var result = Setup(PrototypeRef, DataSetRow, textureFactory);
        if (result)
            Build();
        return result;
    }

    public virtual void Delete()
    {
        QueueFree();
    }

    /// <summary>
    /// Process a Command object
    /// </summary>
    /// <param name="command"></param>
    /// <returns>true if action consumed by object. Else false</returns>
    public virtual bool ProcessCommand(VisualCommand command)
    {
        if (command == VisualCommand.ToggleLock)
        {
            Locked = !Locked;

            return true;
        }

        if (command == VisualCommand.RotateCcw)
        {
            var begin = Transform;
            SubmitRotation(ProjectService.Instance.RotationStep);
            return true;
        }

        if (command == VisualCommand.RotateCw)
        {
            var begin = Transform;
            SubmitRotation(-1 * ProjectService.Instance.RotationStep);
            return true;
        }

        if (command == VisualCommand.Delete)
        {
            // Deletion is now handled by a DeleteEffect event
            return false;
        }

        if (command == VisualCommand.Refresh)
        {
            Refresh(TextureFactory);
            return true;
        }

        if (command == VisualCommand.Duplicate)
        {
            EventBus.Instance.Publish(
                new SpawnPrototypeEvent { PrototypeRef = PrototypeRef, DataSetRow = DataSetRow }
            );
            return true;
        }

        if (command == VisualCommand.Edit)
        {
            EventBus.Instance.Publish(new EditPrototypeEvent { PrototypeId = PrototypeRef });
        }

        if (command == VisualCommand.MakeUnique)
        {
            EventBus.Instance.Publish(new MakePrototypeUniqueEvent { PrototypeId = PrototypeRef });
        }

        return false;
    }

    /// <summary>
    /// Override in subclasses that support quantity-based commands (e.g. Draw N, Deal N).
    /// The base implementation returns an unconsumed response.
    /// <paramref name="quantity"/> is Int32.MaxValue when the user chose "All".
    /// </summary>
    public virtual void ProcessCommandWithQuantity(VisualCommand command, int quantity) { }

    /// <summary>
    /// Implemented by flippable components.
    /// </summary>
    public virtual void AnimateFlip(bool faceUp) { }

    protected TextureFactory TextureFactory;

    public virtual List<MenuCommand> GetMenuCommands()
    {
        var l = new List<MenuCommand>();

        //l.Add(new MenuCommand(VisualCommand.ToggleLock, Locked));
        switch (Layer)
        {
            case LayerType.Normal:
                l.Add(new MenuCommand(VisualCommand.Freeze));
                l.Add(new MenuCommand(VisualCommand.Tuck));
                break;
            case LayerType.Frozen:
                l.Add(new MenuCommand(VisualCommand.Unfreeze));
                break;
            case LayerType.Tucked:
                l.Add(new MenuCommand(VisualCommand.Untuck));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        l.Add(new MenuCommand(VisualCommand.RotateCw));
        l.Add(new MenuCommand(VisualCommand.RotateCcw));
        l.Add(new MenuCommand(VisualCommand.Delete));
        l.Add(new MenuCommand(VisualCommand.Refresh));
        l.Add(new MenuCommand(VisualCommand.Duplicate));
        l.Add(new MenuCommand(VisualCommand.Edit));
        l.Add(new MenuCommand(VisualCommand.MakeUnique));
        return l;
    }

    public virtual string ComponentName { get; set; }

    public virtual Guid PrototypeRef { get; set; }

    public virtual SnowportId Reference { get; set; } = Snowport.Clock.Create();

    /// <summary>
    /// The id of the most recent event that moved this component.
    /// When an outdated move event arrives, it will be ignored.
    /// </summary>
    public SnowportId LastMoveId { get; set; } = SnowportId.Empty;

    /// <summary>
    /// Which row in the DataSet supplies the data for templating
    /// </summary>
    public virtual string DataSetRow { get; set; }

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

    public enum LayerType
    {
        Normal,
        Frozen,
        Tucked,
    }

    private LayerType _layer = LayerType.Normal;

    public LayerType Layer
    {
        get => _layer;
        set
        {
            if (_layer == value)
                return;

            _layer = value;
            SyncRequired = true;
        }
    }

    /// <summary>
    /// The component's stacking order. Computed from a <see cref="global::ZOrder"/> rather than a
    /// dense integer; components are created on top (their creation event id) and reordered by
    /// transform events. Higher sits physically on top.
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

    protected bool _locked;

    public virtual bool Locked
    {
        get => Layer == LayerType.Frozen;
        set
        {
            if (_locked != value)
            {
                _locked = value;
                SyncRequired = true;
                LockChanged();
            }
        }
    }

    protected void LockChanged()
    {
        UpdateHighlight();
        IsClickSelected = false;
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

            _isClickSelected = !Locked && value;

            UpdateHighlight();
        }
    }

    public bool IsSelected => IsMouseSelected || IsClickSelected;

    protected virtual void UpdateHighlight()
    {
        if (HighlightMesh == null)
            return;

        HighlightMesh.Visible = IsSelected && !NeverHighlight && !Locked;
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
        if (!IsDragging)
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

    private bool _isDragging;

    public abstract GeometryInstance3D DragMesh { get; }

    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (!CanDrag)
                return;
            if (_isDragging == value)
                return;
            _isDragging = value;
            if (!value)
            {
                IsMouseSelected = false;
            }
        }
    }

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

    public virtual void DropObjects(IEnumerable<VisualComponentBase> dragObjects) { }

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
        Board,
        Container,
        Hand,
    }

    private ComponentLocation _location;

    public ComponentLocation Location
    {
        get => _location;
        set
        {
            _location = value;
            LogicalVisible = value == ComponentLocation.Board;
        }
    }

    private void SubmitRotation(float degreesAboutY)
    {
        var t = TransformEffect.Capture(this);
        t.Rotation = Rotation + new Vector3(0, Mathf.DegToRad(degreesAboutY), 0);
        EventSynchronizer.Instance?.Submit(TableEvent.Now(null, t));
    }

    private bool _logicalVisible = true;

    /// <summary>
    /// Whether or not the component is visible for all players, unrelated to zones.
    /// This is the value that syncs across the network.
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

    public bool SuppressSync { get; set; }

    private bool _syncRequired;

    public bool SyncRequired
    {
        get => _syncRequired;
        set
        {
            _syncRequired = value;
            if (value && !SuppressSync && !ExcludeFromSync)
                EventBus.Instance.Publish(new ComponentPropertyChangedEvent(this));
        }
    }

    /// <summary>
    /// If true, this component will not be synced to other nodes
    /// </summary>
    public bool ExcludeFromSync { get; set; }

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

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

public partial class GameObjects : Node
{
    [Export]
    private DragPlane _dragPlane;

    [Export]
    private DragSelectRectangle _selectionRectangle;

    [Export]
    private int _stackingUpdateFrames = 3; //Test hack to avoid issue with stacking not seeing colliders

    /// <summary>
    /// How fast a dropped component slides into place, in cm per second.
    /// </summary>
    [Export(PropertyHint.Range, "1,30,1,suffix:cm/s")]
    public float DropSpeed { get; set; } = 10f;

    [Signal]
    public delegate void CameraActivationEventHandler(bool cameraActivated);

    /// <summary>
    /// Raised the frame after components change, once they have synced and the table's
    /// caches are rebuilt. Signals to views to update their state.
    /// </summary>
    [Signal]
    public delegate void TableChangedEventHandler();

    private int _stackingUpdateRequired;

    // set when a component changes, and handled once at the start of the next frame
    private bool _tableChanged;

    private GameController _gameController;

    /// <summary>
    /// Holds the nodes for the current game's components, and any spawn previews.
    /// </summary>
    private Table _table;

    /// <summary>The current game's components.</summary>
    private Godot.Collections.Array<Node> ComponentNodes => _table.GetChildren();

    private CursorMode _cursorMode;

    public CursorMode CursorMode
    {
        get => _cursorMode;
        private set
        {
            if (_cursorMode == CursorMode.Drag && value != CursorMode.Drag)
                CloseDragGroup();
            _cursorMode = value;
        }
    }

    /// <summary>
    /// The group Id for attaching to the player's drag, or <see cref="SnowTag.Empty"/>.
    /// </summary>
    public SnowportId DragGroup { get; private set; }

    /// <summary>
    /// Closes the drag's undo group, if it's still open, with an event that does nothing else.
    /// This is just a fallback.
    /// </summary>
    private void CloseDragGroup()
    {
        if (DragGroup == SnowportId.Empty)
            return;
        RecordService.Instance.Close(DragGroup, DragCommands.Drop);
        DragGroup = SnowportId.Empty;
    }

    /// <summary>
    /// Starts a drag with the records in <paramref name="pickup"/>.
    /// </summary>
    private void StartDrag(IEnumerable<Replicated> pickup, IEnumerable<SnowTag> selected)
    {
        CloseDragGroup();
        var selection = RecordService.Instance.NewSelection<ComponentState>(selected);
        DragGroup = RecordService.Instance.Open(
            DragCommands.Pickup,
            selection == null ? pickup : pickup.Append(selection)
        );
    }

    /// <summary>
    /// Ends a drag with the records in <paramref name="drop"/>.
    /// </summary>
    private void EndDrag(IEnumerable<Replicated> drop)
    {
        RecordService.Instance.Close(DragGroup, DragCommands.Drop, drop);
        DragGroup = SnowportId.Empty;
    }

    public override void _Ready()
    {
        _table = CreateTable();

        EventBus.Instance.Subscribe<ModalDialogOpenedEvent>(OnModalOpened);
        EventBus.Instance.Subscribe<ModalDialogClosedEvent>(OnModalClosed);
    }

    private Table CreateTable()
    {
        var table = new Table { Name = "Table", TextureFactory = TextureFactory };
        table.Changed += () => _tableChanged = true;
        table.ComponentRemoved += OnComponentRemoved;
        AddChild(table);
        return table;
    }

    /// <summary>
    /// Forgets a removed component so nothing touches its freed node.
    /// </summary>
    private void OnComponentRemoved(VisualComponentBase c)
    {
        if (_currentDragDropTarget == c)
            _currentDragDropTarget = null;
        if (_hoveredComponent == c)
        {
            _hoveredComponent = null;
            HoveredComponentChange?.Invoke(this, new HoveredComponentChangeEventArgs(null));
        }
    }

    public void SetGameController(GameController gameController)
    {
        _gameController = gameController;
    }

    private void OnModalClosed()
    {
        _modalOpen = false;
    }

    private bool _modalOpen;

    private void OnModalOpened()
    {
        _modalOpen = true;
    }

    public VisualComponentBase GetComponent(SnowTag reference) => _table.GetComponent(reference);

    /// <summary>
    /// All components contained in <paramref name="containerRef"/> in ZOrder.
    /// Works for containers and player hands.
    /// </summary>
    public IEnumerable<VisualComponentBase> GetContainedComponents(SnowTag containerRef) =>
        Nodes(
            RecordService
                .Instance.Get<ComponentState>(s => s.ContainerRef == containerRef)
                .OrderBy(s => s.ZOrder)
        );

    /// <summary>The nodes for the given records, skipping any that have none yet.</summary>
    private IEnumerable<VisualComponentBase> Nodes(IEnumerable<ComponentState> records) =>
        records.Select(s => GetComponent(s.Id)).Where(c => c != null);

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (_stackingUpdateRequired > 0)
        {
            _stackingUpdateRequired--;

            if (_stackingUpdateRequired <= 0)
            {
                UpdateStackingHeights();
                _stackingUpdateRequired = 0;
            }
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Components sync at the end of a frame, so their changes are handled in the next.
        if (_tableChanged)
        {
            _tableChanged = false;
            RebuildContainerCaches();
            QueueStackingUpdate();
            EmitSignal(SignalName.TableChanged);
        }

        ProcessActiveDrags();
        RecomputeZones();

        switch (CursorMode)
        {
            case CursorMode.Spawn:
                HandleSpawnMode();
                break;
            case CursorMode.Drag:
                HandleDrag();
                break;
            case CursorMode.DragSelect:
                HandleDragSelection();
                break;
            default:
                HandleNormalMode();
                break;
        }

        UpdateHoveredComponent();
    }

    private void UpdateHoveredComponent()
    {
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vcb && vcb.IsHovered)
            {
                if (_hoveredComponent != vcb)
                {
                    _hoveredComponent = vcb;
                    HoveredComponentChange?.Invoke(this, new HoveredComponentChangeEventArgs(vcb));
                    return;
                }

                return;
            }
        }

        if (_hoveredComponent == null)
            return;

        _hoveredComponent = null;
        HoveredComponentChange?.Invoke(this, new HoveredComponentChangeEventArgs(null));
    }

    private VisualComponentBase _hoveredComponent;

    public event EventHandler<HoveredComponentChangeEventArgs> HoveredComponentChange;

    // Specific function to handle normal mode mouse event only outside of GUI elements
    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);

        if (CursorMode == CursorMode.Spawn)
        {
            if (@event.IsActionPressed("spawn_component"))
            {
                CreateComponents(_spawnComponents.Select(s => s.Component));
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed("exit_mode"))
            {
                ExitSpawnMode();
                QueueStackingUpdate();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (
            CursorMode == CursorMode.Normal
            && @event is InputEventMouseButton buttonEvent
            && buttonEvent.Pressed
        )
        {
            if (buttonEvent.ButtonIndex == MouseButton.Left)
            {
                var go = GetHoveredObject();
                if (go == null)
                {
                    StartDragSelection();
                }
                else
                {
                    if (go.IsDrawSelected && go is VisualComponentGroup vcg)
                    {
                        StartDraw(vcg.DragDraw(1));
                    }
                    else
                    {
                        EnterDragMode(go);
                    }
                }
            }
            else if (
                buttonEvent.ButtonIndex == MouseButton.Right
                && GetHoveredObject() is { } go
                && !Selection.Contains(go.Reference)
            )
            {
                SetSelection([go.Reference]);
            }
        }
        else if (@event.IsActionPressed("component_preview"))
        {
            var cp = GetHoveredObject();
            if (cp != null)
            {
                EventBus.Instance.Publish(new ShowComponentPreviewDialogEvent(cp));
            }
        }
    }

    #region Components


    private void AddComponentToScene(VisualComponentBase component)
    {
        if (component.Reference == SnowTag.Empty)
        {
            GD.PrintErr("component somehow lost its SnowTag");
            return;
        }

        _table.AddChild(component);
        RecordService.Instance.SyncNow(component);

        QueueStackingUpdate();
    }

    private void CreateComponents(IEnumerable<VisualComponentBase> components)
    {
        var records = new List<Replicated>();
        var stamp = Snowport.Clock.Create();
        int suborder = 0;

        foreach (var component in components)
        {
            var self = ComponentState.Capture(component) with
            {
                Id = Snowport.Clock.CreateTag(),
                ZOrder = new ZOrder(ZTarget.Top, suborder++, stamp),
            };
            records.Add(self);

            // The stack goes above the component, bottom first.
            foreach (var s in component.GetSpawnStack(self))
            {
                records.Add(s with { ZOrder = new ZOrder(ZTarget.Top, suborder++, stamp) });
            }
        }

        RecordService.Instance.Write(records);
    }

    /// <summary>
    /// The nodes of the cards stacked exactly on the deck, top first.
    /// </summary>
    private List<VisualComponentBase> GetStack(VcDeck deck) =>
        deck.State is { } s ? Nodes(RecordService.Instance.TokensOn(s)).ToList() : [];

    #endregion

    #region Hover
    public VisualComponentBase GetHoveredObject()
    {
        return ComponentNodes.FirstOrDefault(n => n is VisualComponentBase { IsHovered: true })
            as VisualComponentBase;
    }

    private VisualComponentBase GetHoveredDropTarget()
    {
        return ComponentNodes.FirstOrDefault(x =>
                x is VisualComponentBase { IsHovered: true, CanAcceptDrop: true, IsDragging: false }
            ) as VisualComponentBase;
    }

    #endregion

    #region Selection
    /// <summary>The components the local player has selected, read from their selection record.</summary>
    public HashSet<SnowTag> Selection =>
        RecordService.Instance.GetSelection<ComponentState>().ToHashSet();

    /// <summary>Replaces the local player's selected components.</summary>
    public void SetSelection(IEnumerable<SnowTag> components) =>
        RecordService.Instance.SetSelection<ComponentState>(components);

    /// <summary>
    /// Previews a box selection on the nodes. Nothing is written until <see cref="EndDragSelection"/>.
    /// </summary>
    private void PreviewSelection(Rect2 area)
    {
        var camera = GetViewport().GetCamera3D();
        foreach (var c in ComponentNodes.OfType<VisualComponentBase>())
        {
            // Zones and hidden components are not selected by marquee selection.
            c.PreviewSelected =
                c is not VcZone
                && c.Visible
                && PointInRect(camera.UnprojectPosition(c.Position), area);
        }
    }
    #endregion

    #region Stacking

    /// <summary>
    /// The table's columns from the last stacking pass,
    /// sorted from highest ceiling to lowest.
    /// Used during dragging to compute component height.
    /// </summary>
    private StackColumns _tableTops = StackColumns.Empty;

    /// <summary>
    /// Rests each table component on the highest top among the components below it.
    /// </summary>
    private void UpdateStackingHeights()
    {
        var table = GetNotDraggingObjects().ToArray();
        var floors = Stacking.Floors(table, out _tableTops);

        for (int i = 0; i < table.Length; i++)
            table[i].MoveToTargetY(floors[i] + (table[i].YHeight / 2f));
    }

    /// <summary>
    /// Stacks each cursor's dragged components among themselves, so a dragged deck keeps its shape.
    /// </summary>
    private void UpdateDragFloors()
    {
        foreach (var group in GetDraggingObjects().GroupBy(c => c.State.ContainerRef))
        {
            var dragged = group.ToArray();
            var floors = Stacking.Floors(dragged, out _);
            for (int i = 0; i < dragged.Length; i++)
                dragged[i].DragFloor = floors[i];
        }
    }

    /// <summary>
    /// The highest top among the table components under a dragged group, which it floats above.
    /// The group is placed around its dragging <paramref name="cursor"/>.
    /// </summary>
    private float DragLift(List<VisualComponentBase> dragged, Vector3 cursor)
    {
        var footprints = dragged
            .Where(c => c.ShapeProfiles.Count > 0)
            .Select(c => new Footprint(0, c, cursor))
            .ToList();
        if (footprints.Count == 0)
            return 0;

        var bounds = footprints[0].Bounds;
        foreach (var f in footprints)
            bounds = bounds.Merge(f.Bounds);

        float lift = 0;
        for (int column = 0; column < _tableTops.Count; column++)
        {
            if (_tableTops.Top(column) <= lift)
                break;
            var shape = _tableTops.Shape(column);
            if (!bounds.Intersects(shape.Bounds, includeBorders: true))
                continue;
            if (!footprints.Exists(f => Stacking.Overlaps(f, shape)))
                continue;

            foreach (var (c, top) in _tableTops.Members(column))
            {
                if (top <= lift)
                    break;
                if (IsInstanceValid(c) && c.State is { IsOnTable: true })
                {
                    lift = top;
                    break;
                }
            }
        }

        return lift;
    }

    private void QueueStackingUpdate()
    {
        if (_stackingUpdateRequired == 0)
        {
            _stackingUpdateRequired = _stackingUpdateFrames;
        }
    }
    #endregion

    #region Normal Interaction
    private void HandleNormalMode()
    {
        if (GetHoveredObject() == null)
        {
            Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        }
        else
        {
            Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
        }
    }
    #endregion

    #region Spawn
    /// <summary>The spawn previews and their offsets from the cursor.</summary>
    private List<(VisualComponentBase Component, Vector3 Delta)> _spawnComponents;

    public void EnterSpawnMode(List<(VisualComponentBase Component, Vector3 Delta)> components)
    {
        if (_spawnComponents != null)
            ExitSpawnMode();

        CursorMode = CursorMode.Spawn;
        SetSelection([]);

        _spawnComponents = components;

        foreach (var (c, _) in components)
        {
            c.DimMode(true);
            c.NeverHighlight = true;
            AddComponentToScene(c);
        }
    }

    private void HandleSpawnMode()
    {
        var p = _dragPlane.GetCursorProjection();

        foreach (var (c, delta) in _spawnComponents)
        {
            c.Position = new Vector3(p.X, c.YHeight / 2f, p.Z) + delta;
        }
    }

    private TextureFactory _textureFactory;

    public TextureFactory TextureFactory
    {
        get => _textureFactory;
        set
        {
            _textureFactory = value;
            if (_table != null)
                _table.TextureFactory = value;
        }
    }

    private void ExitSpawnMode()
    {
        foreach (var (c, _) in _spawnComponents)
        {
            c.QueueFree();
        }
        _spawnComponents = null;
        CursorMode = CursorMode.Normal;
    }

    #endregion

    #region Drag

    private static bool HandsEnabled() =>
        RecordService.Instance.Single<ProjectGameSettings>().EnablePlayerHands;

    /// <summary>
    /// Recompute per-viewer zone visibility and control for all components each frame so the
    /// local render mask tracks moving pieces, moving zones, and seat/admin changes.
    /// </summary>
    private void RecomputeZones()
    {
        ZoneService.Recompute(
            ComponentNodes.OfType<VisualComponentBase>(),
            RecordService.Instance.LocalSeat(),
            ZoneService.LocalSeatIsAdmin()
        );
    }

    private void EnterDragMode(VisualComponentBase go)
    {
        // Clicking outside the selection replaces it.
        var selected = Selection.Contains(go.Reference) ? Selection : [go.Reference];

        // Zone control gate.
        if (!go.LocallyMovable)
        {
            SetSelection(selected);
            GD.Print($"Object {go.ComponentName} is not movable by the local player (zone)");
            return;
        }

        var startCursor = _dragPlane.GetCursorProjection();

        // A deck carries the cards stacked on it.
        var dragged = selected
            .Select(GetComponent)
            .Where(o => o is { CanDrag: true })
            .SelectMany(o => o is VcDeck deck ? GetStack(deck).Append(deck) : [o])
            .Distinct();

        BeginDrag(dragged, startCursor, selected);
    }

    /// <summary>
    /// The records that draw the given components into the local player's hold.
    /// </summary>
    public Replicated[] BuildDraw(
        IEnumerable<SnowTag> componentRefs,
        Func<VisualComponentBase, Vector3> rotation = null
    )
    {
        var records = new List<Replicated>();
        // Players without a seat can't drag.
        var cursor = RecordService.Instance.LocalCursor();
        if (cursor == SnowTag.Empty)
            return [];

        var stamp = Snowport.Clock.Create();
        foreach (var r in componentRefs)
        {
            var c = GetComponent(r);
            if (c == null)
                continue;

            var s = ComponentState.Of(c);
            records.Add(
                s with
                {
                    ContainerRef = cursor,
                    // Held under the cursor.
                    Position = Vector3.Zero,
                    Rotation = rotation?.Invoke(c) ?? s.Rotation,
                    ZOrder = new ZOrder(ZTarget.Top, records.Count, stamp),
                }
            );
        }

        return records.ToArray();
    }

    /// <summary>
    /// Starts a drag with the records of a draw, from <see cref="BuildDraw"/>.
    /// </summary>
    public void StartDraw(Replicated[] draw)
    {
        if (draw.Length == 0)
            return;

        StartDrag(draw, draw.Select(r => r.Id));

        CursorMode = CursorMode.Drag;
        _localDragOverHand = false;
    }

    private void BeginDrag(
        IEnumerable<VisualComponentBase> components,
        Vector3 cursor,
        IEnumerable<SnowTag> selected
    )
    {
        // Players without a seat can't drag.
        var holder = RecordService.Instance.LocalCursor();
        var dragged = components
            .Where(o => o != null && holder != SnowTag.Empty)
            .Select(o =>
                ComponentState.Of(o) with
                {
                    ContainerRef = holder,
                    // Held positions are relative to the cursor.
                    Position = o.Position - cursor,
                }
            )
            .ToArray();
        if (dragged.Length == 0)
        {
            SetSelection(selected);
            return;
        }

        CursorMode = CursorMode.Drag;
        _localDragOverHand = false;

        StartDrag(dragged, selected);
    }

    private VisualComponentGroup _currentDragDropTarget;

    private void HandleDrag()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePosition = GetViewport().GetMousePosition();

            // The bottom "hand" strip only intercepts the drag when player hands are enabled
            // and at least one dragged object is a printed component.
            VcToken previewCard = null;
            if (HandsEnabled() && mousePosition.Y > _gameController.HandY)
            {
                foreach (var go in GetLocalDraggingObjects())
                {
                    if (go is VcToken vct)
                    {
                        previewCard = vct;
                        break;
                    }
                }
            }

            if (previewCard != null)
            {
                _gameController.HandManager.ShowDragPreview(previewCard, mousePosition);
                previewCard.LogicalVisible = false;
                Input.SetDefaultCursorShape(Input.CursorShape.CanDrop);
                _localDragOverHand = true;
                return; // don't move the 3D objects while hovering the hand
            }

            _localDragOverHand = false;
            _gameController.HandManager.HideDragPreview();

            //check to see if we are over a VisualComponentGroup that can accept a drop,
            var dragTarget = GetGroupDropTargetUnderDrag();

            if (dragTarget != _currentDragDropTarget)
            {
                _currentDragDropTarget?.DragOverExit();
                if (_currentDragDropTarget != null)
                    _currentDragDropTarget.IsHovered = false;
                _currentDragDropTarget = dragTarget;
                if (_currentDragDropTarget != null)
                    _currentDragDropTarget.IsHovered = true;
            }

            if (dragTarget != null && dragTarget.DragOver(GetLocalDraggingObjects()))
            {
                Input.SetDefaultCursorShape(Input.CursorShape.CanDrop);
            }
            else
            {
                Input.SetDefaultCursorShape(Input.CursorShape.Drag);
            }
        }
        else
        {
            EndDrag();
        }
    }

    private VisualComponentGroup GetGroupDropTargetUnderDrag()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return null;

        var mousePos = GetViewport().GetMousePosition();

        // Exclude every player's dragged objects so the ray passes through them.
        var exclude = new Godot.Collections.Array<Rid>();
        foreach (var dragging in GetDraggingObjects())
            exclude.Add(dragging.GetRid());

        foreach (var o in ComponentNodes)
        {
            if (o is VisualComponentBase vcb && vcb is not VisualComponentGroup)
            {
                exclude.Add(vcb.GetRid());
            }
        }

        var ray = new PhysicsRayQueryParameters3D
        {
            From = camera.ProjectRayOrigin(mousePos),
            To = camera.ProjectRayOrigin(mousePos) + camera.ProjectRayNormal(mousePos) * 500f,
            CollideWithAreas = true,
            CollideWithBodies = false,
            Exclude = exclude,
        };

        var result = GetViewport().FindWorld3D().DirectSpaceState.IntersectRay(ray);
        if (!result.ContainsKey("collider"))
            return null;

        var collider = result["collider"].As<Node>();

        // If the hit node is not itself a VisualComponentGroup, walk up the parent chain
        var group = collider as VisualComponentGroup;

        if (group == null)
        {
            var parent = collider?.GetParent();
            while (parent != null)
            {
                if (parent is VisualComponentGroup g)
                {
                    group = g;
                    break;
                }
                parent = parent.GetParent();
            }
        }

        if (group == null || !group.CanAcceptDrop || group.IsDragging)
            return null;

        return group;
    }

    /// <summary>
    /// The components dragged by any player.
    /// </summary>
    private IEnumerable<VisualComponentBase> GetDraggingObjects()
    {
        var cursors = RecordService.Instance.Cursors();
        return Nodes(
            RecordService.Instance.Get<ComponentState>(s => cursors.Contains(s.ContainerRef))
        );
    }

    /// <summary>
    /// The components dragged by the local player, which is all a local gesture acts on.
    /// </summary>
    private IEnumerable<VisualComponentBase> GetLocalDraggingObjects()
    {
        var cursor = RecordService.Instance.LocalCursor();
        return cursor == SnowTag.Empty
            ? []
            : Nodes(RecordService.Instance.Get<ComponentState>(s => s.ContainerRef == cursor));
    }

    private IEnumerable<VisualComponentBase> GetNotDraggingObjects() =>
        Nodes(RecordService.Instance.Get<ComponentState>(s => s.IsOnTable));

    private void EndDrag()
    {
        _gameController.HandManager.HideDragPreview();

        var mousePosition = GetViewport().GetMousePosition();

        // Only divert to the hand when hands are enabled, the drop was over the hand strip,
        // AND there is at least one printed component to hand off.
        if (HandsEnabled() && mousePosition.Y > _gameController.HandY)
        {
            var dragged = GetLocalDraggingObjects().ToList();
            var toHand = dragged.Where(go => go is VcToken).ToList();

            if (toHand.Count > 0)
            {
                var toBoard = dragged.Where(go => go is not VcToken).ToList();
                SubmitHandDrop(toHand, toBoard, RecordService.Instance.LocalSeat());
                Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                CursorMode = CursorMode.Normal;
                return;
            }
        }

        if (_currentDragDropTarget != null)
        {
            if (_currentDragDropTarget.CanObjectsBeDropped(GetLocalDraggingObjects()))
            {
                RecordService.Instance.Append(
                    DragGroup,
                    DragCommands.Drop,
                    _currentDragDropTarget.DropObjects(GetLocalDraggingObjects())
                );
            }

            _currentDragDropTarget.DragOverExit();
            _currentDragDropTarget.IsHovered = false;
            _currentDragDropTarget = null;
        }
        else
        {
            var hover = GetHoveredDropTarget();
            if (
                hover != null
                && hover.CanAcceptDrop
                && hover.CanObjectsBeDropped(GetLocalDraggingObjects())
            )
            {
                RecordService.Instance.Append(
                    DragGroup,
                    DragCommands.Drop,
                    hover.DropObjects(GetLocalDraggingObjects())
                );
            }
        }

        SubmitDrop(GetLocalDraggingObjects());

        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);

        CursorMode = CursorMode.Normal;
    }

    private void SubmitDrop(IEnumerable<VisualComponentBase> dragged)
    {
        _localDragOverHand = false;

        var ordered = dragged.OrderBy(c => c.State.ZOrder).ToList();
        var (snapX, snapZ) = SnapDelta(ordered);

        var stamp = Snowport.Clock.Create();
        var dropped = new Replicated[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ComponentState.Of(ordered[i]) with
            {
                ContainerRef = SnowTag.Empty,
                Position = ordered[i].Position,
                ZOrder = new ZOrder(ZTarget.Top, i, stamp),
            };
            dropped[i] = s with { X = s.X + snapX, Z = s.Z + snapZ };
        }

        EndDrag(dropped);
    }

    /// <summary>
    /// The offset that snapping should apply to the components.
    /// If a candidate VcToken or deck is found, this offset will snap to a stacked position.
    /// If no candidate is found, returns (0, 0).
    /// </summary>
    private (int X, int Z) SnapDelta(List<VisualComponentBase> dragged)
    {
        var targets = GetNotDraggingObjects()
            .Where(c => c is VcToken or VcDeck)
            .Select(c => ComponentState.TableKey(c.Position))
            .ToList();

        foreach (var card in dragged.OfType<VcToken>())
        {
            var from = ComponentState.TableKey(card.Position);
            var found = false;
            (int X, int Z) nearest = (0, 0);
            long nearestDistance = (long)SnapRange * SnapRange;

            foreach (var to in targets)
            {
                long dx = to.X - from.X;
                long dz = to.Z - from.Z;
                long distance = dx * dx + dz * dz;
                if (distance <= nearestDistance)
                {
                    found = true;
                    nearest = (to.X - from.X, to.Z - from.Z);
                    nearestDistance = distance;
                }
            }

            if (found)
                return nearest;
        }

        return (0, 0);
    }

    /// <summary>How close a dropped card must be to snap, in tenths of a millimeter.</summary>
    private const int SnapRange = 250;

    /// <summary>
    /// Drops components into the hand, ending the drag.
    /// </summary>
    private void SubmitHandDrop(
        List<VisualComponentBase> toHand,
        List<VisualComponentBase> toBoard,
        int seat
    )
    {
        _localDragOverHand = false;

        var records = new List<Replicated>(toHand.Count + toBoard.Count);
        var stamp = Snowport.Clock.Create();

        for (int i = 0; i < toHand.Count; i++)
            records.Add(
                PlayerHandService.Instance.MovedToHand(ComponentState.Of(toHand[i]), seat, i, stamp)
            );

        for (int i = 0; i < toBoard.Count; i++)
            records.Add(
                ComponentState.Of(toBoard[i]) with
                {
                    ContainerRef = SnowTag.Empty,
                    Position = toBoard[i].Position,
                    ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                }
            );

        EndDrag(records);
    }

    #endregion

    #region Drag Selection
    private void StartDragSelection()
    {
        SetSelection([]);
        CursorMode = CursorMode.DragSelect;
        _selectionRectangle.StartDragSelect();
    }

    private void HandleDragSelection()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            PreviewSelection(_selectionRectangle.CurRectangle);
        }
        else
        {
            EndDragSelection();
        }
    }

    /// <summary>Selects what the box covers in one event. A click without a drag deselects.</summary>
    private void EndDragSelection()
    {
        CursorMode = CursorMode.Normal;
        _selectionRectangle.StopDragSelect();

        var nodes = ComponentNodes.OfType<VisualComponentBase>().ToList();
        SetSelection(nodes.Where(c => c.PreviewSelected == true).Select(c => c.Reference));
        foreach (var c in nodes)
            c.PreviewSelected = null;
    }
    #endregion

    private static bool PointInRect(Vector2 point, Rect2 rect)
    {
        //normalize in case the size is negative
        float minX = Mathf.Min(rect.Position.X, rect.Position.X + rect.Size.X);
        float maxX = Mathf.Max(rect.Position.X, rect.Position.X + rect.Size.X);

        float minY = Mathf.Min(rect.Position.Y, rect.Position.Y + rect.Size.Y);
        float maxY = Mathf.Max(rect.Position.Y, rect.Position.Y + rect.Size.Y);

        return (point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY);
    }

    #region Multiplayer

    /// <summary>
    /// Resets the game for loading a project.
    /// </summary>
    public void ResetForLoad()
    {
        ResetTable();
        EventSynchronizer.Instance?.Clear();
    }

    /// <summary>
    /// Replaces the table node with an empty one and forgets the local player's gestures.
    /// </summary>
    public void ResetTable()
    {
        // Removed now so its watch stops before the new table's starts.
        var old = _table;
        RemoveChild(old);
        old.QueueFree();
        _table = CreateTable();
        _tableChanged = false;

        _cursorMode = CursorMode.Normal;
        DragGroup = SnowportId.Empty;
        _spawnComponents = null;
        _currentDragDropTarget = null;
        _hoveredComponent = null;
        _stackingUpdateRequired = 0;
    }

    /// <summary>
    /// Refreshes every container's child cache and every deck's count.
    /// </summary>
    private void RebuildContainerCaches()
    {
        foreach (var group in ComponentNodes.OfType<VisualComponentGroup>())
            group.RebuildCache();
        UpdateDeckCounts();
        UpdateDragFloors();
    }

    /// <summary>
    /// Sets each deck's card count.
    /// </summary>
    private void UpdateDeckCounts()
    {
        // A held deck keeps its count, since its cards leave the table with it.
        foreach (var deck in ComponentNodes.OfType<VcDeck>())
            if (deck.State is { } d && !RecordService.Instance.IsBeingDragged(d))
                deck.SetCount(RecordService.Instance.TokensOn(d).Count);
    }

    private bool _localDragOverHand;

    private void ProcessActiveDrags()
    {
        var cursors = PresenceSynchronizer.Instance;
        if (cursors == null)
            return;

        foreach (var group in GetDraggingObjects().GroupBy(c => c.State.ContainerRef))
        {
            if (group.Key == RecordService.Instance.LocalCursor() && _localDragOverHand)
                continue;

            if (!cursors.TryGetCursor(group.Key, out var cursor))
                continue;

            // Follow the cursor, then float the group, keeping its shape, above what's below.
            var dragged = group.ToList();
            foreach (var c in dragged)
            {
                var offset = c.State.PositionAt(0);
                c.Position = new Vector3(cursor.X + offset.X, c.Position.Y, cursor.Z + offset.Z);
            }

            float lift = DragLift(dragged, cursor);
            foreach (var c in dragged)
            {
                c.Position = c.Position with { Y = lift + c.DragFloor + c.YHeight };
                c.LogicalVisible = true;
            }
        }
    }

    #endregion
}

public class HoveredComponentChangeEventArgs : EventArgs
{
    public HoveredComponentChangeEventArgs(VisualComponentBase component)
    {
        Component = component;
    }

    public VisualComponentBase Component { get; set; }
}

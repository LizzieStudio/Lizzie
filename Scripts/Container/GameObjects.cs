using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class GameObjects : Node
{
    [Export]
    private DragPlane _dragPlane;

    [Export]
    private DragSelectRectangle _selectionRectangle;

    [Export]
    private int _stackingUpdateFrames = 3; //Test hack to avoid issue with stacking not seeing colliders

    [Signal]
    public delegate void CameraActivationEventHandler(bool cameraActivated);

    /// <summary>
    /// Raised after an event has been fully applied to the table.
    /// Signals to views to update their state.
    /// </summary>
    [Signal]
    public delegate void TableChangedEventHandler();

    private int _stackingUpdateRequired;

    /// <summary>
    /// Upserts whose prototype aren't yet available.
    /// </summary>
    private readonly Dictionary<SnowTag, ComponentEffect> _pendingSpawns = new();

    /// <summary>
    /// The id of the event that last wrote each component.
    /// </summary>
    private readonly Dictionary<SnowTag, SnowportId> _lastWrite = new();

    private GameController _gameController;

    /// <summary>
    /// Container which contains all components for the current game.
    /// </summary>
    private Node _table;

    /// <summary>The current game's components.</summary>
    private Godot.Collections.Array<Node> ComponentNodes => _table.GetChildren();

    public CursorMode CursorMode { get; private set; }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    public override void _Ready()
    {
        _table = new Node { Name = "Table" };
        AddChild(_table);

        EventBus.Instance.Subscribe<LocalPlayerJoinedGameEvent>(OnLocalPlayerJoinedGame);

        EventBus.Instance.Subscribe<ModalDialogOpenedEvent>(OnModalOpened);
        EventBus.Instance.Subscribe<ModalDialogClosedEvent>(OnModalClosed);
        EventBus.Instance.Subscribe<QueueStackingUpdateEvent>(QueueStackingUpdate);

        if (EventSynchronizer.Instance != null)
            EventSynchronizer.Instance.Applied += ApplyEvent;
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

    public VisualComponentBase GetComponent(SnowTag reference)
    {
        return ComponentNodes
            .OfType<VisualComponentBase>()
            .FirstOrDefault(c => c.Reference == reference);
    }

    /// <summary>
    /// All components contained in <paramref name="containerRef"/> in ZOrder.
    /// Works for containers and player hands.
    /// </summary>
    public IEnumerable<VisualComponentBase> GetContainedComponents(SnowTag containerRef)
    {
        return ComponentNodes
            .OfType<VisualComponentBase>()
            .Where(vc => vc.ContainerRef == containerRef)
            .OrderBy(vc => vc.ZOrder);
    }

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
            case CursorMode.PopupMenu:
                HandlePopupMenu();
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
                CreateComponents(_spawnComponents);
                QueueStackingUpdate();
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
            if (buttonEvent.ButtonIndex == MouseButton.Right && IsAnyObjectHovered())
            {
                StartPopupMenu();
            }
            else if (buttonEvent.ButtonIndex == MouseButton.Left)
            {
                var go = GetMouseSelectedObject();
                if (go == null)
                {
                    DeselectComponents();
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


    public void AddComponentToScene(VisualComponentBase component)
    {
        if (component.Reference == SnowTag.Empty)
        {
            GD.PrintErr("component somehow lost its SnowTag");
            return;
        }

        _table.AddChild(component);
        ProjectService.Instance.SyncNow(component);

        QueueStackingUpdate();
    }

    public void CreateComponents(IEnumerable<VisualComponentBase> components)
    {
        var effects = new List<Effect>();
        var stamp = Snowport.Clock.Create();
        int suborder = 0;

        foreach (var component in components)
        {
            var containerRef = Snowport.Clock.CreateTag();

            var childEffects = component.GetSpawnChildEffects(containerRef).ToList();

            effects.AddRange(childEffects);
            effects.Add(
                new ComponentEffect(
                    ComponentState.Capture(component) with
                    {
                        Id = containerRef,
                        ZOrder = new ZOrder(ZTarget.Top, suborder++, stamp),
                    }
                )
            );
        }

        EventSynchronizer.Instance?.Submit(TableEvent.Now(null, effects.ToArray()));
    }

    public void DeleteComponents(IEnumerable<VisualComponentBase> components)
    {
        var effects = components.SelectMany(c => c.GetDespawnEffects());

        EventSynchronizer.Instance?.Submit(TableEvent.Now(null, effects.ToArray()));
    }

    /// <summary>
    /// Captures every component on the table, including spawns waiting on their prototype.
    /// </summary>
    public ComponentEffect[] GenerateCatchupEffects()
    {
        return ComponentNodes
            .OfType<VisualComponentBase>()
            .Select(ComponentEffect.Capture)
            .Concat(_pendingSpawns.Values)
            .ToArray();
    }

    public Dictionary<SnowTag, int> PrototypeCounts()
    {
        Dictionary<SnowTag, int> counts = new();
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vcb && vcb.PrototypeRef != SnowTag.Empty && vcb.Visible)
            {
                if (!counts.TryAdd(vcb.PrototypeRef, 1))
                {
                    counts[vcb.PrototypeRef]++;
                }
            }
        }
        return counts;
    }

    #endregion

    #region Hover
    public bool IsAnyObjectHovered()
    {
        return ComponentNodes.Any(n => n is VisualComponentBase { IsHovered: true });
    }

    public VisualComponentBase GetHoveredObject()
    {
        return ComponentNodes.FirstOrDefault(n => n is VisualComponentBase { IsHovered: true })
            as VisualComponentBase;
    }

    public VisualComponentBase GetHoveredDropTarget()
    {
        return ComponentNodes.FirstOrDefault(x =>
                x is VisualComponentBase { IsHovered: true, CanAcceptDrop: true, IsDragging: false }
            ) as VisualComponentBase;
    }

    #endregion

    #region Selection
    public bool IsAnyObjectSelected()
    {
        return ComponentNodes.Any(n => n is VisualComponentBase { IsSelected: true });
    }

    public bool IsAnyObjectMouseSelected()
    {
        return ComponentNodes.Any(n => n is VisualComponentBase { IsMouseSelected: true });
    }

    public VisualComponentBase GetSelectedObject()
    {
        return ComponentNodes.FirstOrDefault(n => n is VisualComponentBase { IsSelected: true })
            as VisualComponentBase;
    }

    public IEnumerable<VisualComponentBase> GetSelectedObjects()
    {
        return ComponentNodes
            .Where(n => n is VisualComponentBase { IsSelected: true })
            .Cast<VisualComponentBase>();
    }

    public VisualComponentBase GetMouseSelectedObject()
    {
        return ComponentNodes.FirstOrDefault(n =>
                n is VisualComponentBase { IsMouseSelected: true }
            ) as VisualComponentBase;
    }

    public void SelectComponents(Rect2 area)
    {
        foreach (var go in ComponentNodes)
        {
            // Zones and hidden components are not selected by marquee selection.
            if (go is VisualComponentBase vcb and not VcZone && vcb.Visible)
            {
                var screenPos = GetViewport().GetCamera3D().UnprojectPosition(vcb.Position);
                vcb.IsClickSelected = PointInRect(screenPos, area);
            }
        }
    }

    public void DeselectComponents()
    {
        foreach (var go in ComponentNodes)
        {
            if (go is VisualComponentBase v)
            {
                v.IsClickSelected = false;
            }
        }
    }
    #endregion

    #region Stacking

    /// <summary>
    /// Sends the components to the top or bottom of the ZOrder.
    /// </summary>
    private void Reorder(IEnumerable<VisualComponentBase> components, ZTarget target)
    {
        var ordered = components.Where(c => c is not VcZone).OrderBy(c => c.ZOrder).ToList();
        if (ordered.Count == 0)
            return;

        var stamp = Snowport.Clock.Create();
        var arr = new Effect[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
            arr[i] = new ComponentEffect(
                ComponentState.Capture(ordered[i]) with
                {
                    ZOrder = new ZOrder(target, i, stamp),
                }
            );

        EventSynchronizer.Instance?.Submit(TableEvent.Now(null, arr));
    }

    /// <summary>
    /// Determines the maximum y-stacking height for the dragged objects.
    /// </summary>
    /// <returns></returns>
    private float GetDragHeight()
    {
        var _dragObjects = GetDraggingObjects().ToList();
        if (!_dragObjects.Any())
            return 0;

        var children = ComponentNodes;

        //make a list of all the objects that are 'in line' with the shapes of the moving objects
        float maxFloor = 0;

        foreach (var d in _dragObjects)
        {
            foreach (var c in children)
            {
                if (c is VisualComponentBase vcb)
                {
                    if (_dragObjects.Contains(vcb))
                        continue;

                    if (CheckOverlap(d, vcb))
                    {
                        maxFloor = Mathf.Max(maxFloor, vcb.Position.Y + vcb.YHeight / 2);
                    }
                }
            }
        }

        return maxFloor;
    }

    private void UpdateStackingHeights()
    {
        //var children = ComponentNodes;
        var children = GetNotDraggingObjects().ToArray();

        //this dictionary keeps track of objects that are below a certain object. The key is the object id
        //(in the children array), and the list elements are the object ids of the things that are under it.
        Dictionary<int, List<int>> underneath = new();
        for (int i = 0; i < children.Length; i++)
        {
            var ci = children[i] as VisualComponentBase;

            if (ci == null)
            {
                GD.PrintErr($"{children[i].Name} not VCB");
                continue;
            }

            if (ci.ShapeProfiles.Count == 0)
                continue;

            for (int j = 0; j < children.Length; j++)
            {
                var cj = children[j];

                if (cj == null)
                {
                    GD.PrintErr($"{children[j].Name} not VCB");
                    continue;
                }

                if (cj.ZOrder < ci.ZOrder && CheckOverlap(ci, cj)) //lower zOrders are below other items
                {
                    //GD.PrintErr($"Area {i} overlaps Area {j}");
                    //add to dictionary
                    if (underneath.ContainsKey(i))
                    {
                        underneath[i].Add(j);
                    }
                    else
                    {
                        underneath.Add(i, new List<int> { j });
                    }
                }
            }
        }

        //loop through all the objects and check the dictionary (which is in Z order) and stack
        //The y coordinate is set to the sum of all of the YHeight values below it.
        //We loop through all the children (and not just the UNDERNEATH dictionary entries)
        //in case there's nothing underneath them. The dictionary only contains items with something below
        //them

        for (int i = 0; i < children.Length; i++)
        {
            var ci = children[i] as VisualComponentBase;
            if (ci is null)
                continue;

            float floor = 0;

            if (underneath.TryGetValue(i, out var elements))
            {
                foreach (var o in elements)
                {
                    if (children[o] is VisualComponentBase co)
                        floor += co.YHeight;
                }
            }

            //GD.Print($"New pos for {i}: {floor + (ci.YHeight / 2f)}");

            ci.MoveToTargetY(floor + (ci.YHeight / 2f));
            //ci.Position = new Vector3(ci.Position.X, floor + (ci.YHeight / 2f), ci.Position.Z);
        }
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
        if (Input.IsActionJustPressed("move_to_top"))
            Reorder(GetSelectedObjects(), ZTarget.Top);
        if (Input.IsActionJustPressed("move_to_bottom"))
            Reorder(GetSelectedObjects(), ZTarget.Bottom);
        if (Input.IsActionJustPressed("component_delete"))
            DeleteComponents(GetSelectedObjects());
    }
    #endregion

    #region Popup Menu
    public void PopupClosed()
    {
        EndPopupMenu();
    }

    private void StartPopupMenu()
    {
        CursorMode = CursorMode.PopupMenu;

        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2I v = new((int)Math.Floor(mouse.X), (int)Math.Floor(mouse.Y));

        var vch = GetSelectedObjects();
        if (!vch.Any() && GetHoveredObject() != null)
        {
            vch = Enumerable.Repeat(GetHoveredObject(), 1);
        }

        //EmitSignal(SignalName.ShowComponentPopup, v, new Godot.Collections.Array<VisualComponentBase>(vch));
        ShowComponentPopup?.Invoke(this, new ShowComponentPopupEventArgs(v, vch));
    }

    public event EventHandler<ShowComponentPopupEventArgs> ShowComponentPopup;

    private void HandlePopupMenu() { }

    private void EndPopupMenu()
    {
        CursorMode = CursorMode.Normal;
    }
    #endregion

    #region Spawn
    private List<VisualComponentBase> _spawnComponents;

    public void EnterSpawnMode(List<VisualComponentBase> components)
    {
        if (_spawnComponents != null)
            ExitSpawnMode();

        CursorMode = CursorMode.Spawn;

        _spawnComponents = components;

        foreach (var c in components)
        {
            c.DimMode(true);
            c.NeverHighlight = true;
            AddComponentToScene(c);
        }
    }

    private void HandleSpawnMode()
    {
        var p = _dragPlane.GetCursorProjection();

        foreach (var c in _spawnComponents)
        {
            c.Position = new Vector3(p.X, c.YHeight / 2f, p.Z) + c.SpawnDelta;
        }
    }

    public TextureFactory TextureFactory { get; set; }

    private void ExitSpawnMode()
    {
        foreach (var c in _spawnComponents)
        {
            c.QueueFree();
        }
        _spawnComponents = null;
        CursorMode = CursorMode.Normal;
    }

    #endregion

    #region Drag

    private static bool HandsEnabled() => ProjectService.Instance.Settings.Value.EnablePlayerHands;

    /// <summary>
    /// Recompute per-viewer zone visibility and control for all components each frame so the
    /// local render mask tracks moving pieces, moving zones, and seat/admin changes.
    /// </summary>
    private void RecomputeZones()
    {
        ZoneService.Recompute(
            ComponentNodes.OfType<VisualComponentBase>(),
            PlayerHandService.LocalSeatIndex(),
            ZoneService.LocalSeatIsAdmin()
        );
    }

    private void EnterDragMode(VisualComponentBase go)
    {
        // Zone control gate.
        if (!go.LocallyMovable)
        {
            GD.Print($"Object {go.ComponentName} is not movable by the local player (zone)");
            return;
        }

        var startCursor = _dragPlane.GetCursorProjection();

        BeginDrag(GetSelectedObjects().Where(o => o.CanDrag), startCursor);
    }

    /// <summary>
    /// Builds the event that draws the given components into the local cursor container.
    /// </summary>
    public TableEvent BuildDrawEvent(
        IEnumerable<SnowTag> componentRefs,
        Func<VisualComponentBase, Vector3> rotation = null
    )
    {
        if (PresenceSynchronizer.Instance == null)
            return null;
        var cursorContainer = PresenceSynchronizer.Instance.LocalCursorRef;

        var effects = new List<Effect>();
        var stamp = Snowport.Clock.Create();
        foreach (var r in componentRefs)
        {
            var c = GetComponent(r);
            if (c == null)
                continue;

            effects.Add(
                new ComponentEffect(
                    ComponentState.Capture(c) with
                    {
                        Location = VisualComponentBase.ComponentLocation.Cursor,
                        ContainerRef = cursorContainer,
                        // Position is the cursor-relative offset while held.
                        Position = c.SpawnDelta,
                        Rotation = rotation?.Invoke(c) ?? c.Rotation,
                        ZOrder = new ZOrder(ZTarget.Top, effects.Count, stamp),
                    }
                )
            );
        }

        return effects.Count == 0 ? null : TableEvent.Now(new MoveAction(), effects.ToArray());
    }

    public void StartDraw(TableEvent drawEvent)
    {
        if (drawEvent == null)
            return;

        EventSynchronizer.Instance?.Submit(drawEvent);

        CursorMode = CursorMode.Drag;
        _localDragOverHand = false;
    }

    private void BeginDrag(IEnumerable<VisualComponentBase> components, Vector3 cursor)
    {
        if (PresenceSynchronizer.Instance == null)
            return;
        var cursorContainer = PresenceSynchronizer.Instance.LocalCursorRef;

        var dragged = components
            .Where(o => o != null)
            .Select(o => new ComponentEffect(
                ComponentState.Capture(o) with
                {
                    Location = VisualComponentBase.ComponentLocation.Cursor,
                    ContainerRef = cursorContainer,
                    // Position with a cursor container is relative to the cursor.
                    Position = o.Position - cursor,
                }
            ))
            .ToArray();
        if (dragged.Length == 0)
            return;

        CursorMode = CursorMode.Drag;
        _localDragOverHand = false;

        EventSynchronizer.Instance?.Submit(TableEvent.Now(new MoveAction(), dragged));
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
                foreach (var go in GetDraggingObjects())
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
                {
                    _currentDragDropTarget.IsHovered = false;
                    _currentDragDropTarget.IsMouseSelected = false;
                }
                _currentDragDropTarget = dragTarget;
                if (_currentDragDropTarget != null)
                {
                    _currentDragDropTarget.IsHovered = true;
                    _currentDragDropTarget.IsMouseSelected = true;
                }
            }

            if (dragTarget != null && dragTarget.DragOver(GetDraggingObjects()))
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

        // Exclude all dragging objects so the ray passes through them
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

    private IEnumerable<VisualComponentBase> GetDraggingObjects()
    {
        foreach (var n in ComponentNodes)
        {
            if (n is VisualComponentBase { IsDragging: true } p)
            {
                yield return p;
            }
        }
    }

    private IEnumerable<VisualComponentBase> GetNotDraggingObjects()
    {
        foreach (var n in ComponentNodes)
        {
            if (
                n is VisualComponentBase { Location: VisualComponentBase.ComponentLocation.Table } p
            )
            {
                yield return p;
            }
        }
    }

    private void EndDrag()
    {
        _gameController.HandManager.HideDragPreview();

        var mousePosition = GetViewport().GetMousePosition();

        // Only divert to the hand when hands are enabled, the drop was over the hand strip,
        // AND there is at least one printed component to hand off.
        if (HandsEnabled() && mousePosition.Y > _gameController.HandY)
        {
            var dragged = GetDraggingObjects().ToList();
            var toHand = dragged.Where(go => go is VcToken).ToList();

            if (toHand.Count > 0)
            {
                var toBoard = dragged.Where(go => go is not VcToken).ToList();
                SubmitHandDrop(toHand, toBoard, PlayerHandService.LocalSeatIndex());
                Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                CursorMode = CursorMode.Normal;
                return;
            }
        }

        if (_currentDragDropTarget != null)
        {
            if (_currentDragDropTarget.CanObjectsBeDropped(GetDraggingObjects()))
            {
                var dropEvent = _currentDragDropTarget.DropObjects(GetDraggingObjects());
                if (dropEvent != null)
                    EventSynchronizer.Instance?.Submit(dropEvent);
            }

            _currentDragDropTarget.DragOverExit();
            _currentDragDropTarget.IsHovered = false;
            _currentDragDropTarget.IsMouseSelected = false;
            _currentDragDropTarget = null;
        }
        else
        {
            var hover = GetHoveredDropTarget();
            if (
                hover != null
                && hover.CanAcceptDrop
                && hover.CanObjectsBeDropped(GetDraggingObjects())
            )
            {
                var dropEvent = hover.DropObjects(GetDraggingObjects());
                if (dropEvent != null)
                    EventSynchronizer.Instance?.Submit(dropEvent);
            }
        }

        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);

        CursorMode = CursorMode.Normal;

        SubmitDrop(GetDraggingObjects());
    }

    private void SubmitDrop(IEnumerable<VisualComponentBase> dragged)
    {
        _localDragOverHand = false;

        var stamp = Snowport.Clock.Create();
        var dropped = dragged
            .Select(
                (component, index) =>
                    new ComponentEffect(
                        ComponentState.Capture(component) with
                        {
                            Location = VisualComponentBase.ComponentLocation.Table,
                            ContainerRef = SnowTag.Empty,
                            Position = component.Position,
                            ZOrder = new ZOrder(ZTarget.Top, index, stamp),
                        }
                    )
            )
            .ToArray();

        EventSynchronizer.Instance?.Submit(TableEvent.Now(new MoveAction(), dropped));
    }

    /// <summary>
    /// Submits the event to send components to the hand.
    /// </summary>
    private void SubmitHandDrop(
        List<VisualComponentBase> toHand,
        List<VisualComponentBase> toBoard,
        int seat
    )
    {
        _localDragOverHand = false;

        var effects = new List<Effect>(toHand.Count + toBoard.Count);
        var stamp = Snowport.Clock.Create();

        for (int i = 0; i < toHand.Count; i++)
            effects.Add(PlayerHandService.Instance.MoveEffect(toHand[i], seat, i, stamp));

        for (int i = 0; i < toBoard.Count; i++)
            effects.Add(
                new ComponentEffect(
                    ComponentState.Capture(toBoard[i]) with
                    {
                        Location = VisualComponentBase.ComponentLocation.Table,
                        ContainerRef = SnowTag.Empty,
                        Position = toBoard[i].Position,
                        ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                    }
                )
            );

        EventSynchronizer.Instance?.Submit(TableEvent.Now(new MoveAction(), effects.ToArray()));
    }

    #endregion

    #region Drag Selection
    private void StartDragSelection()
    {
        CursorMode = CursorMode.DragSelect;
        _selectionRectangle.StartDragSelect();
    }

    private void HandleDragSelection()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            SelectComponents(_selectionRectangle.CurRectangle);
        }
        else
        {
            EndDragSelection();
        }
    }

    private void EndDragSelection()
    {
        CursorMode = CursorMode.Normal;
        _selectionRectangle.StopDragSelect();
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

    private static bool CheckOverlap(VisualComponentBase comp1, VisualComponentBase comp2)
    {
        foreach (var offsetShape1 in comp1.ShapeProfiles)
        {
            // Rotate the offset by the component's rotation, then add to component position
            var rotatedOffset1 = offsetShape1.Offset.Rotated(comp1.Rotation.Y);
            var pos1 = new Vector2(comp1.Position.X, comp1.Position.Z) + rotatedOffset1;
            Transform2D t1 = new(comp1.Rotation.Y, pos1);

            foreach (var offsetShape2 in comp2.ShapeProfiles)
            {
                var rotatedOffset2 = offsetShape2.Offset.Rotated(comp2.Rotation.Y);
                var pos2 = new Vector2(comp2.Position.X, comp2.Position.Z) + rotatedOffset2;
                Transform2D t2 = new(comp2.Rotation.Y, pos2);

                if (offsetShape1.Shape.Collide(t1, offsetShape2.Shape, t2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    #region Multiplayer

    private void OnLocalPlayerJoinedGame() => ReplaceWithNewGame();

    /// <summary>
    /// Resets the game for loading a project.
    /// </summary>
    public void ResetForLoad()
    {
        var old = _table;
        _table = new Node { Name = "Table" };
        AddChild(_table);
        old.QueueFree();

        CursorMode = CursorMode.Normal;
        _spawnComponents = null;
        _currentDragDropTarget = null;
        _hoveredComponent = null;
        _stackingUpdateRequired = 0;

        _pendingSpawns.Clear();
        _lastWrite.Clear();
        EventSynchronizer.Instance?.Clear();
        PresenceSynchronizer.Instance?.Clear();
    }

    /// <summary>
    /// Resets the game for joining an existing game.
    /// </summary>
    public void ReplaceWithNewGame()
    {
        ResetForLoad();
        ProjectService.Instance?.NewGame();
    }

    /// <summary>
    /// The single entry point for the events.
    /// </summary>
    private void ApplyEvent(TableEvent e)
    {
        if (EventSynchronizer.Instance?.BulkLoading == true)
            return;

        if (e.Action is UndoAction undo)
        {
            ReconstructForUndoRedo(undo);
            return;
        }

        foreach (var effect in e.Effects)
            if (effect is ComponentEffect ce)
                ApplyUpsert(e.Id, ce);

        RebuildContainerCaches();

        QueueStackingUpdate();

        EmitSignal(SignalName.TableChanged);
    }

    private void ApplyUpsert(SnowportId eventId, ComponentEffect fx)
    {
        var s = fx.State;
        var r = fx.Id;

        // Reject if it's older than the most recent write.
        if (_lastWrite.TryGetValue(r, out var last) && eventId.CompareTo(last) < 0)
            return;
        _lastWrite[r] = eventId;

        var c = GetComponent(r);

        if (s.Deleted)
        {
            RemoveComponent(c);
            _pendingSpawns.Remove(r);
            return;
        }

        if (c == null)
        {
            if (!TryExecuteSpawn(fx))
                AddPendingSpawn(fx);
            return;
        }

        ApplyStateToComponent(c, s, eventId);
    }

    /// <summary>
    /// Detaches a component from the scene synchronously.
    /// </summary>
    private void RemoveComponent(VisualComponentBase c)
    {
        if (c == null)
            return;
        c.GetParent()?.RemoveChild(c);
        c.QueueFree();
    }

    /// <summary>
    /// Applies a write to a component, animating its transition if the write is recent enough.
    /// </summary>
    private static void ApplyStateToComponent(
        VisualComponentBase c,
        ComponentState s,
        SnowportId writeId
    )
    {
        c.Location = s.Location;
        c.ContainerRef = s.ContainerRef;
        // While held, Position carries the cursor-relative offset.
        if (s.Location == VisualComponentBase.ComponentLocation.Cursor)
            c.CursorOffset = s.PositionAt(c.CursorOffset.Y);
        else
            c.Position = s.PositionAt(c.Position.Y);
        if (
            s.Transition == Transition.None
            || !c.PlayTransition(s, Snowport.Clock.MsecSince(writeId))
        )
            c.Rotation = s.Rotation;
        c.ZOrder = s.ZOrder;
    }

    /// <summary>
    /// Spawns a component from a transform.
    /// </summary>
    private bool TryExecuteSpawn(ComponentEffect fx)
    {
        var s = fx.State;
        var proto = ProjectService.Instance.GetIncludingDeleted<Prototype>(s.PrototypeRef);
        if (proto == null)
            return false;

        var path = Utility.ComponentTypeToScenePath(
            proto.Type,
            proto.Parameters,
            s.DataSetRowIndex,
            s.DataSetRowId
        );
        var scene = GD.Load<PackedScene>(path).Instantiate();

        if (scene is not VisualComponentBase vcb)
        {
            GD.PrintErr($"Spawned scene for {s.PrototypeRef} is not a VisualComponentBase");
            return true;
        }

        vcb.Reference = fx.Id;
        vcb.PrototypeRef = s.PrototypeRef;
        _table.AddChild(vcb);
        vcb.SpawnBuild(s, TextureFactory);
        vcb.Position = s.PositionAt(vcb.YHeight / 2f);
        QueueStackingUpdate();

        return true;
    }

    /// <summary>
    /// Holds a spawn until its prototype arrives.
    /// </summary>
    private void AddPendingSpawn(ComponentEffect fx)
    {
        _pendingSpawns[fx.Id] = fx;
        ProjectService.Instance.ForceSync(this);
    }

    /// <summary>
    /// Retries pending spawns, then waits on the prototypes that are still missing.
    /// </summary>
    private void Sync(IRecordReader R)
    {
        RetryPendingSpawns();
        R.Get<Prototype>(_pendingSpawns.Values.Select(p => p.State.PrototypeRef));
    }

    private void RetryPendingSpawns()
    {
        if (_pendingSpawns.Count == 0)
            return;

        var pending = _pendingSpawns.Values.ToList();
        _pendingSpawns.Clear();

        foreach (var p in pending)
            if (!TryExecuteSpawn(p))
                _pendingSpawns[p.Id] = p; // prototype still not available
    }

    #region Undo

    /// <summary>
    /// Rebuilds the whole table from the event log.
    /// </summary>
    public void RebuildFromLog()
    {
        var log = EventSynchronizer.Instance?.EventLog;
        if (log == null)
            return;

        var undone = UndoLog.ComputeUndone(log);

        var tags = new HashSet<SnowTag>();
        foreach (var ev in log.Values)
        foreach (var fx in ev.Effects)
            if (fx is ComponentEffect ce)
                tags.Add(ce.Id);

        foreach (var r in tags)
            ReconstructComponent(r, log, undone);

        RetryPendingSpawns();
        RebuildContainerCaches();
        QueueStackingUpdate();
        EmitSignal(SignalName.TableChanged);
    }

    /// <summary>
    /// Applies an undo or redo by searching backwards for the components that need updating.
    /// </summary>
    private void ReconstructForUndoRedo(UndoAction undo)
    {
        var log = EventSynchronizer.Instance?.EventLog;
        if (log == null)
            return;

        var undone = UndoLog.ComputeUndone(log);
        foreach (var r in UndoLog.ResolveAffectedComponents(log, undo.Target))
            ReconstructComponent(r, log, undone);

        RebuildContainerCaches();
        QueueStackingUpdate();
        EmitSignal(SignalName.TableChanged);
    }

    /// <summary>
    /// Reconstructs one component from the log by scanning backward for its most recent write.
    /// </summary>
    private void ReconstructComponent(
        SnowTag r,
        OrderedDictionary<SnowportId, TableEvent> log,
        HashSet<SnowportId> undone
    )
    {
        ComponentEffect winner = null;
        SnowportId winnerId = SnowportId.Empty;

        // The log is sorted, so the first write found is the newest.
        for (int i = log.Count - 1; i >= 0 && winner == null; i--)
        {
            var e = log.GetAt(i).Value;
            if (undone.Contains(e.Id))
                continue;

            foreach (var fx in e.Effects)
                if (
                    fx is ComponentEffect ce
                    && ce.Id == r
                    && ce.State.Location != VisualComponentBase.ComponentLocation.Cursor
                )
                {
                    winner = ce;
                    winnerId = e.Id;
                    break;
                }
        }

        _pendingSpawns.Remove(r);
        var live = GetComponent(r);

        if (winner == null)
        {
            RemoveComponent(live);
            _lastWrite.Remove(r);
            return;
        }

        _lastWrite[r] = winnerId;

        if (winner.State.Deleted)
        {
            RemoveComponent(live);
            return;
        }

        if (live == null)
        {
            if (!TryExecuteSpawn(winner))
                AddPendingSpawn(winner);
            return;
        }

        ApplyStateToComponent(live, winner.State, winnerId);
    }

    #endregion

    /// <summary>
    /// Refreshes every container's child cache from the source-of-truth,
    /// <see cref="VisualComponentBase.ContainerRef"/>.
    /// </summary>
    private void RebuildContainerCaches()
    {
        var all = ComponentNodes.OfType<VisualComponentBase>().ToList();
        foreach (var group in all.OfType<VisualComponentGroup>())
            group.RebuildCache(all);
    }

    private bool _localDragOverHand;

    private void ProcessActiveDrags()
    {
        var dragHeight = GetDragHeight();
        var cursors = PresenceSynchronizer.Instance;
        if (cursors == null)
            return;

        var localCursorRef = cursors.LocalCursorRef;

        foreach (var n in ComponentNodes)
        {
            if (n is not VisualComponentBase { IsDragging: true } c)
                continue;

            bool isLocal = localCursorRef != SnowTag.Empty && c.ContainerRef == localCursorRef;

            if (isLocal && _localDragOverHand)
                continue;

            if (!cursors.TryGetCursorByContainer(c.ContainerRef, out var cursor))
                continue;

            c.Position = new Vector3(
                cursor.X + c.CursorOffset.X,
                dragHeight + c.YHeight,
                cursor.Z + c.CursorOffset.Z
            );
            c.LogicalVisible = true;
        }
    }

    #endregion
}

public class ShowComponentPopupEventArgs : EventArgs
{
    public ShowComponentPopupEventArgs(
        Vector2I position,
        IEnumerable<VisualComponentBase> components
    )
    {
        Position = position;
        Components = components;
    }

    public Vector2I Position { get; set; }
    public IEnumerable<VisualComponentBase> Components { get; set; }
}

public class HoveredComponentChangeEventArgs : EventArgs
{
    public HoveredComponentChangeEventArgs(VisualComponentBase component)
    {
        Component = component;
    }

    public VisualComponentBase Component { get; set; }
}

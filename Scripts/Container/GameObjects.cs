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
    private readonly Dictionary<SnowTag, PendingSpawn> _pendingSpawns = new();

    /// <summary>
    /// Each component's latest applied transform.
    /// </summary>
    private readonly Dictionary<SnowTag, SnowportId> _lastWrite = new();

    /// <summary>
    /// The id of the most recent <see cref="TableClearEffect"/>.
    /// Any transform older than this is rejected.
    /// </summary>
    private SnowportId _clearBarrier = SnowportId.Empty;

    private GameController _gameController;

    /// <summary>
    /// Container which contains all components for the current game.
    /// </summary>
    private Node _table;

    /// <summary>The current game's components.</summary>
    private Godot.Collections.Array<Node> ComponentNodes => _table.GetChildren();

    public CursorMode CursorMode { get; private set; }

    public override void _Ready()
    {
        _table = new Node { Name = "Table" };
        AddChild(_table);

        EventBus.Instance.Subscribe<LocalPlayerJoinedGameEvent>(OnLocalPlayerJoinedGame);
        EventBus.Instance.Subscribe<ProjectChangedEvent>(_ => RetryPendingSpawns());
        ProjectService.Instance.Prototypes.Observe(_ => RetryPendingSpawns());

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

        component.Build();

        QueueStackingUpdate();
    }

    public void CreateComponents(IEnumerable<VisualComponentBase> components)
    {
        var effects = new List<Effect>();

        foreach (var component in components)
        {
            var containerRef = Snowport.Clock.CreateTag();

            var childEffects = component.GetSpawnChildEffects(containerRef).ToList();

            var state = new VcSyncDto(component);

            effects.AddRange(childEffects);
            effects.Add(
                new ComponentEffect
                {
                    Id = containerRef,
                    PrototypeRef = component.PrototypeRef,
                    State = state,
                }
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
    /// Captures the current table as a set of creation effects for a late joiner.
    /// </summary>
    public Effect[] GenerateCatchupEffects()
    {
        var effects = new List<Effect>();

        effects.AddRange(
            ComponentNodes
                .OfType<VisualComponentBase>()
                .Select(component =>
                    (Effect)
                        new ComponentEffect
                        {
                            Id = component.Reference,
                            PrototypeRef = component.PrototypeRef,
                            State = new VcSyncDto(component),
                        }
                )
        );

        return effects.ToArray();
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
        if (target == ZTarget.Unset)
            return;

        var ordered = components.Where(c => c is not VcZone).OrderBy(c => c.ZOrder).ToList();
        if (ordered.Count == 0)
            return;

        var arr = new Effect[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            var e = ComponentEffect.Capture(ordered[i]);
            e.State.ZOrder = new ZOrder(target, i, SnowportId.Empty);
            arr[i] = e;
        }

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

    /// <summary>
    /// This routine takes a base name (like "Cube") and checks to
    /// see if there is an object already called that in the scene.
    /// If there is, it appends (xx) where xx is a unique number
    /// </summary>
    /// <param name="baseName"></param>
    /// <returns></returns>
    public string CreateUniqueName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return baseName;

        //for simplicity pull all the existing names into a List
        var names = new List<string>();
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vcb)
                names.Add(vcb.ComponentName);
        }

        //if we're already unique, we're done
        if (names.All(x => x != baseName))
            return baseName;

        //try to append
        for (int i = 1; i < 1000; i++)
        {
            var newName = $"{baseName}({i})";
            if (names.All(x => x != newName))
                return newName;
        }

        //put the above in a loop to avoid an infinite loop in case of horrible weirdness
        GD.PrintErr($"Error creating new name for {baseName}");
        return $"{baseName}(ERROR)";
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
        foreach (var r in componentRefs)
        {
            var c = GetComponent(r);
            if (c == null)
                continue;

            var e = ComponentEffect.Capture(c);
            e.State.Location = VisualComponentBase.ComponentLocation.Cursor;
            e.State.ContainerRef = cursorContainer;
            // Position is the cursor-relative offset while held.
            e.State.Position = c.SpawnDelta;
            if (rotation != null)
                e.State.Rotation = rotation(c);
            e.State.ZOrder = new ZOrder(ZTarget.Top, effects.Count, SnowportId.Empty);
            effects.Add(e);
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
            .Select(o =>
            {
                var e = ComponentEffect.Capture(o);
                e.State.Location = VisualComponentBase.ComponentLocation.Cursor;
                e.State.ContainerRef = cursorContainer;
                // Position with a cursor container is relative to the cursor.
                e.State.Position = o.Position - cursor;
                return e;
            })
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

        var dropped = dragged
            .Select(
                (component, index) =>
                {
                    var effect = ComponentEffect.Capture(component);
                    effect.State.Location = VisualComponentBase.ComponentLocation.Table;
                    effect.State.ContainerRef = SnowTag.Empty;
                    effect.State.Position = component.Position;
                    effect.State.ZOrder = new ZOrder(ZTarget.Top, index, SnowportId.Empty);
                    return effect;
                }
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

        for (int i = 0; i < toHand.Count; i++)
            effects.Add(PlayerHandService.Instance.MoveEffect(toHand[i], seat, i));

        for (int i = 0; i < toBoard.Count; i++)
        {
            var effect = ComponentEffect.Capture(toBoard[i]);
            effect.State.Location = VisualComponentBase.ComponentLocation.Table;
            effect.State.ContainerRef = SnowTag.Empty;
            effect.State.Position = toBoard[i].Position;
            effect.State.ZOrder = new ZOrder(ZTarget.Top, i, SnowportId.Empty);
            effects.Add(effect);
        }

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
        _clearBarrier = SnowportId.Empty;
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

        // Roll and flip animate, so don't snap to their transform.
        var animated = e.Action is RollAction or FlipAction;

        foreach (var effect in e.Effects)
            if (effect is ComponentEffect ce)
                ApplyUpsert(e.Id, ce, animated);

        // We clear the table after upserts because the table clear ignores nodes
        // from the same event as it. This means that nodes that exist before and
        // after aren't recreated from scratch, but just kept.
        if (e.Effects.Any(fx => fx is TableClearEffect))
            ApplyTableClear(e.Id);

        RebuildContainerCaches();

        switch (e.Action)
        {
            case RollAction:
                foreach (var fx in e.Effects.OfType<ComponentEffect>())
                    if (GetComponent(fx.Id) is VcDie die)
                        die.AnimateRoll(fx.State.Rotation);
                break;
            case FlipAction:
                foreach (var fx in e.Effects.OfType<ComponentEffect>())
                    GetComponent(fx.Id)?.AnimateFlip(fx.State.Rotation);
                break;
        }

        QueueStackingUpdate();

        EmitSignal(SignalName.TableChanged);
    }

    private void ApplyUpsert(SnowportId eventId, ComponentEffect fx, bool animated)
    {
        var s = fx.State ?? new VcSyncDto();
        var r = fx.Id;
        var writeId = s.LastMoveId == SnowportId.Empty ? eventId : s.LastMoveId;

        // reject anything before a GameStateSwitchAction (for late arrivals)
        if (writeId.CompareTo(_clearBarrier) < 0)
            return;

        // Reject if it's older than the most recent transform.
        if (_lastWrite.TryGetValue(r, out var last) && writeId.CompareTo(last) < 0)
            return;
        _lastWrite[r] = writeId;

        var c = GetComponent(r);

        if (s.Location == VisualComponentBase.ComponentLocation.Deleted)
        {
            RemoveComponent(c);
            _pendingSpawns.Remove(r);
            return;
        }

        if (c == null)
        {
            if (!TryExecuteSpawn(writeId, fx))
                _pendingSpawns[r] = new PendingSpawn(writeId, fx);
            return;
        }

        ApplyStateToComponent(c, writeId, s, animated);
    }

    /// <summary>
    /// Removes every component older than the clear event and sets the clear barrier so
    /// late arriving upserts are rejected.
    /// </summary>
    private void ApplyTableClear(SnowportId eventId)
    {
        if (_clearBarrier.CompareTo(eventId) < 0)
            _clearBarrier = eventId;

        foreach (var c in ComponentNodes.OfType<VisualComponentBase>().ToList())
        {
            if (c.LastMoveId.CompareTo(eventId) >= 0)
                continue;
            var r = c.Reference;
            RemoveComponent(c);
            _lastWrite.Remove(r);
            _pendingSpawns.Remove(r);
        }

        // clear buffered spawns too
        foreach (
            var key in _pendingSpawns
                .Where(kv => kv.Value.WriteId.CompareTo(eventId) < 0)
                .Select(kv => kv.Key)
                .ToList()
        )
            _pendingSpawns.Remove(key);
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

    /// <summary>Applies a transform to a component.</summary>
    private static void ApplyStateToComponent(
        VisualComponentBase c,
        SnowportId writeId,
        VcSyncDto s,
        bool animated
    )
    {
        c.LastMoveId = writeId;
        c.Location = s.Location;
        c.ContainerRef = s.ContainerRef;
        // While held, Position carries the cursor-relative offset.
        if (s.Location == VisualComponentBase.ComponentLocation.Cursor)
            c.CursorOffset = s.Position;
        else
            c.Position = s.Position;
        if (!animated)
            c.Rotation = s.Rotation;

        // Only restack when the transform sets a zorder.
        if (s.ZOrder.Target != ZTarget.Unset)
            c.ZOrder =
                s.ZOrder.LastEvent == SnowportId.Empty
                    ? new ZOrder(s.ZOrder.Target, s.ZOrder.Suborder, writeId)
                    : s.ZOrder;
    }

    /// <summary>
    /// Spawns a component from a transform.
    /// </summary>
    private bool TryExecuteSpawn(SnowportId writeId, ComponentEffect fx)
    {
        if (!ProjectService.Instance.Prototypes.Records.TryGetValue(fx.PrototypeRef, out var proto))
            return false;

        var s = fx.State ?? new VcSyncDto();

        var path = Utility.ComponentTypeToScenePath(
            proto.Type,
            proto.Parameters,
            s.DataSetRowIndex,
            s.DataSetRowId
        );
        var scene = GD.Load<PackedScene>(path).Instantiate();

        if (scene is not VisualComponentBase vcb)
        {
            GD.PrintErr($"Spawned scene for {fx.PrototypeRef} is not a VisualComponentBase");
            return true;
        }

        vcb.Reference = fx.Id;
        vcb.PrototypeRef = fx.PrototypeRef;
        _table.AddChild(vcb);
        vcb.SpawnBuild(fx.PrototypeRef, s, TextureFactory);
        QueueStackingUpdate();

        vcb.LastMoveId = writeId;
        if (s.ZOrder.Target == ZTarget.Unset)
            vcb.ZOrder = new ZOrder(ZTarget.Top, 0, writeId);
        else if (s.ZOrder.LastEvent == SnowportId.Empty)
            vcb.ZOrder = new ZOrder(s.ZOrder.Target, s.ZOrder.Suborder, writeId);

        return true;
    }

    /// <summary>
    /// Re-attempts to spawn nodes that didn't have the prototype yet.
    /// </summary>
    private void RetryPendingSpawns()
    {
        if (_pendingSpawns.Count == 0)
            return;

        var pending = _pendingSpawns.Values.ToList();
        _pendingSpawns.Clear();

        foreach (var p in pending)
            if (!TryExecuteSpawn(p.WriteId, p.Effect))
                _pendingSpawns[p.Effect.Id] = p; // prototype still not available
    }

    private record PendingSpawn(SnowportId WriteId, ComponentEffect Effect);

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

        _clearBarrier = SnowportId.Empty;
        var tags = new HashSet<SnowTag>();
        foreach (var ev in log.Values)
        {
            if (
                !undone.Contains(ev.Id)
                && UndoLog.HasTableClear(ev)
                && _clearBarrier.CompareTo(ev.Id) < 0
            )
                _clearBarrier = ev.Id;

            foreach (var fx in ev.Effects)
                if (fx is ComponentEffect ce)
                    tags.Add(ce.Id);
        }

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

        _clearBarrier = SnowportId.Empty;
        foreach (var ev in log.Values)
        {
            if (
                !undone.Contains(ev.Id)
                && UndoLog.HasTableClear(ev)
                && _clearBarrier.CompareTo(ev.Id) < 0
            )
            {
                _clearBarrier = ev.Id;
            }
        }

        RebuildContainerCaches();
        QueueStackingUpdate();
        EmitSignal(SignalName.TableChanged);
    }

    private static SnowportId WriteIdOf(ComponentEffect ce, SnowportId eventId) =>
        ce.State.LastMoveId == SnowportId.Empty ? eventId : ce.State.LastMoveId;

    /// <summary>
    /// Reconstructs one component from the log by scanning backward for the most recent transforms.
    /// </summary>
    private void ReconstructComponent(
        SnowTag r,
        OrderedDictionary<SnowportId, TableEvent> log,
        HashSet<SnowportId> undone
    )
    {
        ComponentEffect winner = null;
        SnowportId winnerId = SnowportId.Empty;
        ComponentEffect zwin = null;
        SnowportId zwinId = SnowportId.Empty;
        SnowportId barrier = SnowportId.Empty;

        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log.GetAt(i).Value;
            if (undone.Contains(e.Id))
                continue;

            bool clearHere = UndoLog.HasTableClear(e);
            if (clearHere && barrier.CompareTo(e.Id) < 0)
                barrier = e.Id;

            ComponentEffect ce = null;
            foreach (var fx in e.Effects)
                if (fx is ComponentEffect x && x.Id == r)
                {
                    ce = x;
                    break;
                }

            if (
                ce?.State != null
                && ce.State.Location != VisualComponentBase.ComponentLocation.Cursor
            )
            {
                if (
                    winner == null
                    || WriteIdOf(ce, e.Id).CompareTo(WriteIdOf(winner, winnerId)) > 0
                )
                {
                    winner = ce;
                    winnerId = e.Id;
                }
                if (
                    ce.State.ZOrder.Target != ZTarget.Unset
                    && (zwin == null || WriteIdOf(ce, e.Id).CompareTo(WriteIdOf(zwin, zwinId)) > 0)
                )
                {
                    zwin = ce;
                    zwinId = e.Id;
                }
            }

            if (winner != null && zwin != null)
                break;
            if (clearHere)
                break;
        }

        _pendingSpawns.Remove(r);
        var live = GetComponent(r);

        var wId = winner == null ? SnowportId.Empty : WriteIdOf(winner, winnerId);
        var cleared = winner != null && barrier.CompareTo(wId) > 0;

        if (
            winner == null
            || cleared
            || winner.State.Location == VisualComponentBase.ComponentLocation.Deleted
        )
        {
            RemoveComponent(live);
            if (winner == null || cleared)
                _lastWrite.Remove(r);
            else
                _lastWrite[r] = wId;
            return;
        }

        _lastWrite[r] = wId;

        var z =
            zwin != null
                ? (
                    zwin.State.ZOrder.LastEvent == SnowportId.Empty
                        ? new ZOrder(
                            zwin.State.ZOrder.Target,
                            zwin.State.ZOrder.Suborder,
                            WriteIdOf(zwin, zwinId)
                        )
                        : zwin.State.ZOrder
                )
                : new ZOrder(ZTarget.Top, 0, wId);
        var s = new VcSyncDto
        {
            Position = winner.State.Position,
            Rotation = winner.State.Rotation,
            DataSetRowIndex = winner.State.DataSetRowIndex,
            DataSetRowId = winner.State.DataSetRowId,
            Location = winner.State.Location,
            ContainerRef = winner.State.ContainerRef,
            ZOrder = z,
            LastMoveId = wId,
        };

        if (live == null)
        {
            var spawnFx = new ComponentEffect
            {
                Id = r,
                PrototypeRef = winner.PrototypeRef,
                State = s,
            };
            if (!TryExecuteSpawn(wId, spawnFx))
                _pendingSpawns[r] = new PendingSpawn(wId, spawnFx);
            return;
        }

        ApplyStateToComponent(live, wId, s, animated: false);
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

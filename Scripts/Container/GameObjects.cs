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

    private int _stackingUpdateRequired;
    private readonly List<PendingSpawnRequest> _pendingSpawns = new();

    /// <summary>
    /// Components that have been deleted.
    /// </summary>
    private readonly HashSet<SnowportId> _tombstones = new();

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
        EventBus.Instance.Subscribe<DataSetChangedEvent>(OnDataSetChanged);
        EventBus.Instance.Subscribe<TemplateChangedEvent>(OnTemplateChanged);
        EventBus.Instance.Subscribe<ProjectChangedEvent>(OnProjectChanged);
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

    private void OnDataSetChanged(DataSetChangedEvent obj)
    {
        //naive approach for now
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vc)
            {
                vc.ProcessCommand(VisualCommand.Refresh);
            }
        }
    }

    private void OnProjectChanged(ProjectChangedEvent obj)
    {
        //naive approach for now
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vc)
            {
                vc.ProcessCommand(VisualCommand.Refresh);
            }
        }
        RetryPendingSpawns();
    }

    private void OnTemplateChanged(TemplateChangedEvent obj)
    {
        //naive approach for now
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vc)
            {
                vc.ProcessCommand(VisualCommand.Refresh);
            }
        }
    }

    public VisualComponentBase GetComponent(SnowportId reference)
    {
        return ComponentNodes
            .OfType<VisualComponentBase>()
            .FirstOrDefault(vc => vc.Reference == reference);
    }

    /// <summary>
    /// All components contained in <paramref name="containerRef"/> in ZOrder.
    /// Works for containers and player hands.
    /// </summary>
    public IEnumerable<VisualComponentBase> GetContainedComponents(SnowportId containerRef)
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

        /*
        if (_modalOpen) return;
        {
            return; //don't do anything if a modal dialog is open
        }
        */

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
        if (component.Reference == SnowportId.Empty)
        {
            GD.PrintErr("component somehow lost its SnowportId");
            return;
        }

        GD.Print($"Adding component: {component.GetType()} subtype: {component.ComponentType}");

        _table.AddChild(component);
        component.Build();

        QueueStackingUpdate();
    }

    public void CreateComponents(IEnumerable<VisualComponentBase> components)
    {
        var effects = new List<Effect>();

        foreach (var component in components)
        {
            var containerRef = Snowport.Clock.Create();

            var childEffects = component.GetSpawnChildEffects(containerRef).ToList();

            var state = new VcSyncDto(component);

            effects.AddRange(childEffects);
            effects.Add(
                new CreateEffect
                {
                    Id = containerRef,
                    PrototypeRef = component.PrototypeRef,
                    ComponentName = component.ComponentName ?? string.Empty,
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

        var project = ProjectService.Instance.CurrentProject;
        if (project != null)
            foreach (var proto in project.Prototypes.Values)
                effects.Add(new PrototypeEffect { Id = proto.PrototypeRef, Prototype = proto });

        effects.AddRange(
            ComponentNodes
                .OfType<VisualComponentBase>()
                .Select(component =>
                    (Effect)
                        new CreateEffect
                        {
                            Id = component.Reference,
                            PrototypeRef = component.PrototypeRef,
                            ComponentName = component.ComponentName ?? string.Empty,
                            State = new VcSyncDto(component),
                        }
                )
        );

        return effects.ToArray();
    }

    public Dictionary<SnowportId, int> PrototypeCounts()
    {
        Dictionary<SnowportId, int> counts = new();
        foreach (var c in ComponentNodes)
        {
            if (c is VisualComponentBase vcb && vcb.PrototypeRef != SnowportId.Empty && vcb.Visible)
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

    #region GameState

    /// <summary>
    /// Snapshot all VisualComponent children into a named GameState and store it
    /// in the current project.  If a state with the same name already exists it
    /// is overwritten.
    /// </summary>
    public GameState CaptureGameState(string name, string description = "")
    {
        var state = new GameState
        {
            Name = name,
            CapturedAt = DateTime.UtcNow,
            Description = description,
        };

        foreach (var child in ComponentNodes)
        {
            if (child is VisualComponentBase vcb)
            {
                state.Components.Add(GameStateComponent.FromComponent(vcb));
            }
        }

        var project = ProjectService.Instance.CurrentProject;
        if (project != null)
        {
            project.GameStates[name] = state;
            ProjectService.Instance.SaveProject(project);
            EventBus.Instance.Publish(new GameStateChangedEvent());
        }

        GD.Print($"GameState '{name}' captured ({state.Components.Count} components).");
        return state;
    }

    /// <summary>
    /// Restore the scene to a previously captured GameState.  Components that
    /// existed in the snapshot but are no longer in the scene are re-spawned;
    /// components present in the scene but absent from the snapshot are deleted;
    /// components in both are repositioned/updated.
    /// </summary>
    public void RestoreGameState(string name)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null || !project.GameStates.TryGetValue(name, out var state))
        {
            GD.PrintErr($"RestoreGameState: no state named '{name}' found.");
            return;
        }

        RestoreGameState(state);
    }

    /// <summary>
    /// Restore the scene directly from a <see cref="GameState"/> object.
    /// </summary>
    public void RestoreGameState(GameState state)
    {
        if (state == null)
            return;

        // Build a lookup of the saved components by their reference Guid.
        var saved = state.Components.ToDictionary(c => c.ComponentRef);

        // Build a lookup of live components.
        var live = ComponentNodes.OfType<VisualComponentBase>().ToDictionary(c => c.Reference);

        // Update or delete live components.
        foreach (var (refId, component) in live)
        {
            if (saved.TryGetValue(refId, out var entry))
            {
                entry.ApplyToComponent(component);
            }
            else
            {
                // Component not present in snapshot — remove it.
                component.QueueFree();
            }
        }

        // Spawn components that exist in the snapshot but not in the live scene.
        foreach (var (refId, entry) in saved)
        {
            if (live.ContainsKey(refId))
                continue; // Already handled above.

            var project = ProjectService.Instance.CurrentProject;
            if (
                project == null
                || !project.Prototypes.TryGetValue(entry.PrototypeRef, out var proto)
            )
            {
                GD.PrintErr(
                    $"RestoreGameState: prototype {entry.PrototypeRef} not found for component {refId}."
                );
                continue;
            }

            var scenePath = Utility.ComponentTypeToScenePath(
                proto.Type,
                proto.Parameters,
                entry.DataSetRow
            );
            if (string.IsNullOrEmpty(scenePath))
            {
                GD.PrintErr($"RestoreGameState: could not resolve scene for {proto.Type}.");
                continue;
            }

            var scene = GD.Load<PackedScene>(scenePath).Instantiate();
            if (scene is not VisualComponentBase newComponent)
            {
                GD.PrintErr($"RestoreGameState: spawned scene is not a VisualComponentBase.");
                scene.QueueFree();
                continue;
            }

            newComponent.Reference = refId;
            newComponent.PrototypeRef = entry.PrototypeRef;

            entry.ApplyToComponent(newComponent);
            newComponent.Setup(entry.PrototypeRef, entry.DataSetRow, TextureFactory);

            AddComponentToScene(newComponent);
        }

        RebuildContainerCaches();

        GD.Print($"GameState '{state.Name}' restored ({state.Components.Count} entries).");
    }

    /// <summary>
    /// Remove a named GameState from the current project.
    /// </summary>
    public bool DeleteGameState(string name)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return false;

        if (!project.GameStates.Remove(name))
            return false;

        ProjectService.Instance.SaveProject(project);
        EventBus.Instance.Publish(new GameStateChangedEvent());
        GD.Print($"GameState '{name}' deleted.");
        return true;
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
            var t = TransformEffect.Capture(ordered[i]);
            t.ZTarget = target;
            t.ZSuborder = i;
            arr[i] = t;
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

        GD.Print("Collision check complete");

        //uncomment the below to get a printout of the Underneath dictionary

        /*
        foreach (var r in underneath)
        {
            string s = String.Empty;
            foreach (var q in r.Value)
            {
                s += $"{q} ";
            }

            GD.PrintErr($"{r.Key} is above {s}");
        }
        */

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

    private static bool HandsEnabled() =>
        ProjectService.Instance.CurrentProject?.GameSettings?.EnablePlayerHands == true;

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
        IEnumerable<SnowportId> componentRefs,
        Func<VisualComponentBase, Vector3> rotation = null
    )
    {
        if (CursorSynchronizer.Instance == null)
            return null;
        var cursorContainer = CursorSynchronizer.Instance.LocalCursorRef;

        var effects = new List<Effect>();
        foreach (var r in componentRefs)
        {
            var c = GetComponent(r);
            if (c == null)
                continue;

            var t = TransformEffect.Capture(c);
            t.Location = VisualComponentBase.ComponentLocation.Cursor;
            t.ContainerRef = cursorContainer;
            // Position is the cursor-relative offset while held.
            t.Position = c.SpawnDelta;
            if (rotation != null)
                t.Rotation = rotation(c);
            t.ZTarget = ZTarget.Top;
            t.ZSuborder = effects.Count;
            effects.Add(t);
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
        if (CursorSynchronizer.Instance == null)
            return;
        var cursorContainer = CursorSynchronizer.Instance.LocalCursorRef;

        var dragged = components
            .Where(o => o != null)
            .Select(o =>
            {
                var t = TransformEffect.Capture(o);
                t.Location = VisualComponentBase.ComponentLocation.Cursor;
                t.ContainerRef = cursorContainer;
                // Position with a cursor container is relative to the cursor.
                t.Position = o.Position - cursor;
                return t;
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

        GD.Print($"Collider: {collider.Name} Parent: {collider.GetParent()?.Name}");
        // If the hit node is not itself a VisualComponentGroup, walk up the parent chain
        var group = collider as VisualComponentGroup;

        if (group != null)
            GD.Print($"VCG: {group.Name}");

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

        /*
        // Verify the ray hit the DragDropCollider specifically, not another shape on the group
        if (group.DragDropCollider != null)
        {
            int hitShapeIndex = result["shape"].AsInt32();
            int dragDropIndex = 0;
            foreach (var child in group.ComponentNodes)
            {
                if (child is CollisionShape3D cs)
                {
                    if (cs == group.DragDropCollider)
                        break;
                    dragDropIndex++;
                }
            }
            if (hitShapeIndex != dragDropIndex) return null;
        }
        */

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
                    var effect = TransformEffect.Capture(component);
                    effect.Location = VisualComponentBase.ComponentLocation.Table;
                    effect.ContainerRef = SnowportId.Empty;
                    effect.Position = component.Position;
                    effect.ZTarget = ZTarget.Top;
                    effect.ZSuborder = index;
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
            var effect = TransformEffect.Capture(toBoard[i]);
            effect.Location = VisualComponentBase.ComponentLocation.Table;
            effect.ContainerRef = SnowportId.Empty;
            effect.Position = toBoard[i].Position;
            effect.ZTarget = ZTarget.Top;
            effect.ZSuborder = i;
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
    /// Resets the game for joining an existing game.
    /// </summary>
    public void ReplaceWithNewGame()
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
        _tombstones.Clear();
        EventSynchronizer.Instance?.Clear();
        PlayerHandService.Instance?.Clear();

        ProjectService.Instance.CurrentProject?.Prototypes.Clear();
    }

    /// <summary>
    /// The single entry point for the events.
    /// </summary>
    private void ApplyEvent(TableEvent e)
    {
        // Roll and flip animate, so don't snap to their transform.
        var animated = e.Action is RollAction or FlipAction;

        foreach (var effect in e.Effects)
        {
            switch (effect)
            {
                case CreateEffect c:
                    ApplyCreate(e.Id, c);
                    break;
                case DeleteEffect d:
                    ApplyDelete(d);
                    break;
                case TransformEffect t:
                    ApplyTransform(e.Id, t, animated);
                    break;
                case PrototypeEffect p:
                    ApplyPrototype(p);
                    break;
                case PrototypeDeleteEffect pd:
                    ApplyPrototypeDelete(pd);
                    break;
            }
        }

        RebuildContainerCaches();

        switch (e.Action)
        {
            case RollAction:
                foreach (var t in e.Effects.OfType<TransformEffect>())
                    if (GetComponent(t.Id) is VcDie die)
                        die.AnimateRoll(t.Rotation);
                break;
            case FlipAction:
                foreach (var t in e.Effects.OfType<TransformEffect>())
                    GetComponent(t.Id)?.AnimateFlip(t.Rotation);
                break;
        }

        QueueStackingUpdate();
    }

    private void ApplyCreate(SnowportId eventId, CreateEffect c)
    {
        // A delete wins over a create for the same ref, even if it arrived first.
        if (_tombstones.Contains(c.Id))
            return;

        if (GetComponent(c.Id) != null)
            return;

        if (!TryExecuteSpawn(eventId, c))
        {
            GD.Print($"Prototype {c.PrototypeRef} not yet available, queuing spawn for {c.Id}");
            _pendingSpawns.Add(new PendingSpawnRequest(eventId, c));
        }
    }

    private bool TryExecuteSpawn(SnowportId eventId, CreateEffect effect)
    {
        if (
            !ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(
                effect.PrototypeRef,
                out var proto
            )
        )
            return false;

        var syncDto = effect.State ?? new VcSyncDto();

        var path = Utility.ComponentTypeToScenePath(
            proto.Type,
            proto.Parameters,
            syncDto.DataSetRow
        );
        var scene = GD.Load<PackedScene>(path).Instantiate();

        if (scene is not VisualComponentBase vcb)
        {
            GD.PrintErr($"Spawned scene for {effect.PrototypeRef} is not a VisualComponentBase");
            return true; // Fatal data error — do not retry
        }

        vcb.Reference = effect.Id;
        vcb.PrototypeRef = effect.PrototypeRef;

        vcb.SpawnBuild(effect.PrototypeRef, syncDto, TextureFactory);

        // A newly created table component starts on top
        if (vcb.ContainerRef == SnowportId.Empty && syncDto.ZOrder.LastEvent == SnowportId.Empty)
            vcb.ZOrder = new ZOrder(ZTarget.Top, 0, eventId);

        AddComponentToScene(vcb);
        return true;
    }

    /// <summary>
    /// Re-attempts any create effects that were deferred because their prototype was not yet
    /// available on this client. Re-queues any that still cannot be resolved.
    /// </summary>
    private void RetryPendingSpawns()
    {
        if (_pendingSpawns.Count == 0)
            return;

        var pending = _pendingSpawns.ToList();
        _pendingSpawns.Clear();

        foreach (var r in pending)
        {
            if (_tombstones.Contains(r.Effect.Id))
                continue;

            GD.Print($"Retrying spawn for {r.Effect.Id}");
            if (!TryExecuteSpawn(r.EventId, r.Effect))
            {
                GD.PrintErr(
                    $"Still cannot spawn {r.Effect.Id} because prototype {r.Effect.PrototypeRef} is not available"
                );
                _pendingSpawns.Add(r); // Prototype still not available — keep in list
            }
        }
    }

    private record PendingSpawnRequest(SnowportId EventId, CreateEffect Effect);

    /// <summary>
    /// Upserts a prototype definition, refreshes any live components built from it,
    /// and retries spawns that were waiting on it.
    /// </summary>
    private void ApplyPrototype(PrototypeEffect p)
    {
        if (p.Prototype == null)
            return;

        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return;

        project.Prototypes[p.Id] = p.Prototype;

        foreach (var c in ComponentNodes)
            if (c is VisualComponentBase vc && vc.PrototypeRef == p.Id)
                vc.ProcessCommand(VisualCommand.Refresh);

        RetryPendingSpawns();

        EventBus.Instance.Publish(new PrototypeChangedEvent { PrototypeId = p.Id });
    }

    /// <summary>
    /// Removes a prototype definition.
    /// </summary>
    private void ApplyPrototypeDelete(PrototypeDeleteEffect d)
    {
        ProjectService.Instance.CurrentProject?.Prototypes.Remove(d.Id);
    }

    private void ApplyDelete(DeleteEffect d)
    {
        _tombstones.Add(d.Id);
        _pendingSpawns.RemoveAll(r => r.Effect.Id == d.Id);
        GetComponent(d.Id)?.QueueFree();
    }

    private void ApplyTransform(SnowportId eventId, TransformEffect t, bool animated)
    {
        var c = GetComponent(t.Id);

        // this happens when an outdated event arrives
        if (c == null || eventId.CompareTo(c.LastMoveId) < 0)
            return;

        c.LastMoveId = eventId;

        c.Location = t.Location;
        c.ContainerRef = t.ContainerRef;
        // While held, Position carries the cursor-relative offset.
        if (t.Location == VisualComponentBase.ComponentLocation.Cursor)
            c.CursorOffset = t.Position;
        else
            c.Position = t.Position;
        if (!animated)
            c.Rotation = t.Rotation;

        // Only reorder when the effect asks to.
        if (t.ZTarget != ZTarget.Unset)
            c.ZOrder = new ZOrder(t.ZTarget, t.ZSuborder, eventId);
    }

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
        var cursors = CursorSynchronizer.Instance;
        if (cursors == null)
            return;

        var localSource = Snowport.Clock.source;

        foreach (var n in ComponentNodes)
        {
            if (n is not VisualComponentBase { IsDragging: true } c)
                continue;

            var source = c.ContainerRef.source;

            if (source == localSource && _localDragOverHand)
                continue;

            if (!cursors.TryGetCursor(source, out var cursor))
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

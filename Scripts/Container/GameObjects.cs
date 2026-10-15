using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

public partial class GameObjects : Node3D
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

    /// <summary>
    /// <para>How old a write can be and still animate a component, like a drop sliding or a flip turning, in milliseconds.</para>
    /// <para>
    /// An older write jumps into place, so a table catches up to what other players already see:
    /// a write that arrived late, or an undo or cancel putting back an earlier one.
    /// </para>
    /// </summary>
    /// <remarks>
    /// We should actually have specific checks per animation, but this is a stopgap fallback.
    /// </remarks>
    [Export(PropertyHint.Range, "0,2000,10,suffix:ms")]
    public int AnimationCutoff { get; set; } = 500;

    private int _stackingUpdateRequired;

    // set when a component changes, and handled once at the start of the next frame
    private bool _tableChanged;

    private GameController _gameController;

    /// <summary>
    /// Holds the nodes for the current game's components, and any spawn previews.
    /// </summary>
    private Table _table;

    /// <summary>The current game's components.</summary>
    private IReadOnlyList<VisualComponentBase> ComponentNodes => _table.Nodes;

    public CursorMode CursorMode { get; private set; }

    // The local player's drag, from its pickup until Godot's drag ends, or null.
    private HeldDrag _held;

    /// <summary>
    /// The group Id for attaching to the player's drag, or <see cref="SnowportId.Empty"/>.
    /// </summary>
    public SnowportId DragGroup => _held?.Group ?? SnowportId.Empty;

    /// <summary>
    /// Starts a drag of the records in <paramref name="pickup"/>,
    /// which are in stacking order and whose positions are their offsets from the pointer.
    /// The pickup also writes <paramref name="alongside"/>, so a cancel takes them back too.
    /// </summary>
    private void StartDrag(IEnumerable<ComponentState> pickup, IEnumerable<Replicated> alongside)
    {
        var R = RecordService.Instance;
        bool overHand = HandRow.Under(GetViewport()) != null;
        var holder = overHand ? R.LocalHandHold() : R.LocalCursor();
        var stamp = Snowport.Clock.Create();
        var records = pickup
            .Select(
                (s, i) =>
                    s with
                    {
                        ContainerRef = holder,
                        ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                    }
            )
            .ToArray();
        // Players without a seat can't drag.
        if (records.Length == 0 || holder == SnowTag.Empty)
            return;

        _held = new HeldDrag
        {
            Group = R.Open(DragCommands.Pickup, records.Concat(alongside)),
            Components = records.Select(r => r.Id).ToArray(),
        };
        CursorMode = CursorMode.Drag;
        _heldOverHand = overHand;
    }

    /// <summary>
    /// What a press on <paramref name="pressed"/> picks up, in stacking order.
    /// </summary>
    private List<VisualComponentBase> PickedUp(VisualComponentBase pressed)
    {
        var place = pressed.State.ContainerRef;
        return GetSelection()
            .Select(GetComponent)
            .Where(o => o?.State?.ContainerRef == place)
            .SelectMany(o => o is VcDeck deck ? GetStack(deck).Append(deck) : [o])
            .Distinct()
            .OrderBy(o => o.State.ZOrder)
            .ToList();
    }

    /// <summary>
    /// Ends a drag with the records in <paramref name="drop"/>.
    /// </summary>
    private static void EndDrag(HeldDrag held, IEnumerable<Replicated> drop) =>
        RecordService.Instance.Close(held.Group, DragCommands.Drop, drop);

    /// <summary>
    /// Called after the local player's drag ends to perform state cleanup.
    /// </summary>
    private void CleanupDrag()
    {
        if (_held != null)
            RecordService.Instance.CancelIfOpen(_held.Group);
        _held = null;
        CursorMode = CursorMode.Normal;
        _heldOverHand = false;
        _gameController.HandManager.HideDragPreview();
        SetDropTarget(null);
    }

    /// <summary>
    /// The full-screen control under the UI that takes the table's mouse input.
    /// It lets events pass through so that the camera and spawning can be handled in _UnhandledInput.
    /// </summary>
    [Export]
    private Control _tableMouseControl;

    public override void _Ready()
    {
        _table = CreateTable();

        _tableMouseControl.SetDragForwarding(
            Callable.From<Vector2, Variant>(GetDragData),
            Callable.From<Vector2, Variant, bool>((_, data) => data.As<GodotObject>() is HeldDrag),
            Callable.From<Vector2, Variant>(
                (at, data) => DropOnTable((HeldDrag)data.AsGodotObject(), ViewportPoint(at))
            )
        );
        _tableMouseControl.GuiInput += OnTableInput;
    }

    public override void _Notification(int notification)
    {
        if (notification == NotificationDragEnd && CursorMode == CursorMode.Drag)
            CleanupDrag();
        // Godot's drag outlives the window losing focus, potentially causing bugs.
        else if (notification == NotificationApplicationFocusOut && _held != null)
            GetViewport().GuiCancelDrag();
    }

    // A point on the table's control, in the viewport.
    private Vector2 ViewportPoint(Vector2 local) =>
        _tableMouseControl.GetGlobalTransformWithCanvas() * local;

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
        if (_hover.Component == c)
            UpdateHover();
    }

    public void SetGameController(GameController gameController)
    {
        _gameController = gameController;
    }

    public VisualComponentBase GetComponent(SnowTag reference) => _table.GetComponent(reference);

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
            UpdateDragFloors();
            QueueStackingUpdate();
        }

        ProcessActiveDrags();
        RecomputeZones();

        switch (CursorMode)
        {
            case CursorMode.Spawn:
                HandleSpawnMode();
                break;
            case CursorMode.Drag:
                UpdateDrag(_held, GetViewport().GetMousePosition());
                break;
            case CursorMode.DragSelect:
                PreviewSelection(_selectionRectangle.CurRectangle);
                break;
            default:
                HandleNormalMode();
                break;
        }

        UpdateHover();
    }

    public event EventHandler<HoveredComponentChangeEventArgs> HoveredComponentChange;

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

    // What the mouse is over, while nothing is being dragged.
    private TableHit _hover;

    /// <summary>The component under the mouse, on the table or in a hand, or null.</summary>
    public VisualComponentBase GetHoveredObject() => _hover.Component;

    /// <inheritdoc cref="TableHit"/>
    private TableHit GetComponentAtPoint(Vector2 point)
    {
        var (component, shape) = Raycast(point, _ => true);
        return new TableHit
        {
            Component = component,
            DrawArea = component is VisualComponentGroup g && shape == g.DragDropCollider,
        };
    }

    /// <summary>
    /// The closest component under <paramref name="point"/> that <paramref name="pickable"/> allows
    /// and the collision shape it was hit on. Hidden components, what anyone is dragging,
    /// and colliders that aren't <see cref="CollisionObject3D.InputRayPickable"/> are skipped.
    /// </summary>
    private (VisualComponentBase Component, CollisionShape3D Shape) Raycast(
        Vector2 point,
        Func<VisualComponentBase, bool> pickable
    )
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return default;

        var from = camera.ProjectRayOrigin(point);
        var ray = new PhysicsRayQueryParameters3D
        {
            From = from,
            To = from + camera.ProjectRayNormal(point) * 500f,
            CollideWithAreas = true,
            CollideWithBodies = false,
        };
        var skipped = new Godot.Collections.Array<Rid>();
        var space = GetViewport().FindWorld3D().DirectSpaceState;

        // Looks past each skipped hit to the next one along the ray.
        for (int tries = 0; tries < 32; tries++)
        {
            var hit = space.IntersectRay(ray);
            if (!hit.TryGetValue("collider", out var found))
                return default;

            var collider = found.As<CollisionObject3D>();
            if (
                collider.InputRayPickable
                && collider is VisualComponentBase c
                && c.IsVisibleInTree()
                && !c.IsDragging
                && pickable(c)
            )
            {
                var owner = collider.ShapeFindOwner(hit["shape"].AsInt32());
                return (c, collider.ShapeOwnerGetOwner(owner) as CollisionShape3D);
            }

            skipped.Add(hit["rid"].AsRid());
            ray.Exclude = skipped;
        }
        return default;
    }

    private void UpdateHover()
    {
        var hovered = CursorMode == CursorMode.Normal ? GetViewport().GuiGetHoveredControl() : null;
        var hit =
            hovered == _tableMouseControl ? GetComponentAtPoint(GetViewport().GetMousePosition())
            : hovered is HandRow row && row.HoveredCard() is { } card
                ? new TableHit { Component = GetComponent(card) }
            : default;

        // A hand card's node is hidden, so its hover isn't drawn.
        var before = _hover.Component;
        var after = hit.Component;
        if (before != after && before != null)
        {
            before.IsHovered = false;
            before.IsDrawSelected = false;
        }
        if (after != null)
        {
            after.IsHovered = true;
            after.IsDrawSelected = hit.DrawArea;
        }

        _hover = hit;
        if (before != after)
            HoveredComponentChange?.Invoke(this, new HoveredComponentChangeEventArgs(after));
    }

    #endregion

    #region Selection
    /// <summary>The components the local player has selected, read from their selection record.</summary>
    private HashSet<SnowTag> GetSelection() =>
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

    #region Normal Mode
    private void HandleNormalMode()
    {
        if (GetHoveredObject() == null)
        {
            SetCursor(Control.CursorShape.Arrow);
        }
        else
        {
            SetCursor(Control.CursorShape.PointingHand);
        }
    }

    private void SetCursor(Control.CursorShape shape) =>
        _tableMouseControl.MouseDefaultCursorShape = shape;
    #endregion

    #region Mouse Input
    /// <summary>
    /// Godot calls this whenever a press becomes a drag.
    /// <para>
    /// This returns Godot-style data that the rest of the program can use to make more drop targets.
    /// For example, player hands are treated like drop targets, but we may have more in the future.
    /// </para>
    /// </summary>
    private Variant GetDragData(Vector2 point)
    {
        if (CursorMode != CursorMode.Normal)
            return default;

        var at = ViewportPoint(point);
        var hit = GetComponentAtPoint(at);
        if (hit.Component == null)
        {
            StartDragSelection(at);
        }
        else if (hit.DrawArea)
        {
            var draw = ((VisualComponentGroup)hit.Component).DragDraw();
            StartDrag(
                draw,
                RecordService.Instance.NewSelection<ComponentState>(draw.Select(d => d.Id))
            );
        }
        else if (_dragPlane.Project(at) is { } cursor)
        {
            EnterDragMode(hit.Component, cursor);
        }
        return Held();
    }

    /// <summary>
    /// The Godot-style data of what the local player holds, or nothing if they hold nothing.
    /// </summary>
    private Variant Held() => _held != null ? _held : default(Variant);

    private void OnTableInput(InputEvent e)
    {
        if (e is not InputEventMouseButton button)
            return;

        if (
            CursorMode == CursorMode.DragSelect
            && button is { ButtonIndex: MouseButton.Left, Pressed: false }
        )
            EndDragSelection();
        else if (CursorMode == CursorMode.Normal && IsSelectingPress(button))
            Press(GetComponentAtPoint(ViewportPoint(button.Position)).Component?.Reference);
    }

    /// <summary>
    /// Selects what a press on <paramref name="component"/> selects, or deselects for a press on null.
    ///
    /// <para>
    /// A press on a selected component keeps the selection, and anything else replaces the selection.
    /// </para>
    /// </summary>
    public void Press(SnowTag? component)
    {
        var selection = GetSelection();
        SetSelection(
            component is not { } c ? []
            : selection.Contains(c) ? selection
            : [c]
        );
    }

    /// <summary>
    /// Whether <paramref name="button"/> is a press that selects.
    /// </summary>
    public static bool IsSelectingPress(InputEventMouseButton button) =>
        button is { ButtonIndex: MouseButton.Left or MouseButton.Right, Pressed: true };
    #endregion

    #region Spawn
    /// <summary>
    /// The spawn previews and their offsets from the cursor.
    /// </summary>
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
        if (_dragPlane.GetCursorProjection() is not { } p)
            return;

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

    /// <summary>
    /// Drags the selection while keeping their offsets from <paramref name="startCursor"/>.
    /// </summary>
    private void EnterDragMode(VisualComponentBase component, Vector3 startCursor)
    {
        // Zone control gate.
        if (!component.LocallyMovable)
            return;

        StartDrag(
            PickedUp(component)
                .Select(o => ComponentState.Of(o) with { Position = o.Position - startCursor }),
            []
        );
    }

    /// <summary>
    /// Picks up a card from <paramref name="row"/>.
    /// </summary>
    /// <param name="row">The row that was pressed.</param>
    /// <param name="cardRef">The card that was pressed.</param>
    /// <param name="press">Where the press happened, in the viewport.</param>
    /// <returns>The drag, or nothing if the card can't be picked up.</returns>
    public Variant GrabFromHand(HandRow row, SnowTag cardRef, Vector2 press)
    {
        if (CursorMode != CursorMode.Normal || GetComponent(cardRef) is not { } card)
            return default;

        StartDrag(
            PickedUp(card)
                .Select(c =>
                {
                    // In the hand's frame, right on screen is +X and down is +Z.
                    var offset = (row.MiddleOf(c.Reference) - press) / row.PixelsPerCm;
                    return ComponentState.Of(c) with
                    {
                        Position = new Vector3(offset.X, 0, offset.Y),
                    };
                }),
            []
        );
        return Held();
    }

    // The container a drop would go into, outlined while the drag is over it.
    private VisualComponentGroup _currentDragDropTarget;

    // Whether the local player's dragged cards are hovering over the hand.
    private bool _heldOverHand;

    private void UpdateDrag(HeldDrag held, Vector2 point)
    {
        var R = RecordService.Instance;
        var cards = held.Cards(R).ToList();
        // Only cards go into a hand, which opens a gap for them itself.
        var row = cards.Count > 0 ? HandRow.Under(GetViewport()) : null;
        bool overHand = row != null;
        // The card held nearest the pointer, which a turn into the hand snaps.
        var nearest = cards.MinBy(s => s.PositionAt(0).LengthSquared());

        // Over a hand, the held cards move to the hold over the hand, where only the 2D preview shows them.
        if (overHand != _heldOverHand)
        {
            _heldOverHand = overHand;
            var holder = _heldOverHand ? R.LocalHandHold() : R.LocalCursor();
            float turn = HandTurn(nearest, overHand);
            R.Append(
                held.Group,
                cards.Select(s => HandCard.Turned(s, turn) with { ContainerRef = holder })
            );
        }
        if (overHand)
        {
            _gameController.HandManager.ShowDragPreview(
                held.Cards(R).OrderBy(s => s.ZOrder).ToList(),
                row,
                point
            );
            SetDropTarget(null);
            return;
        }
        _gameController.HandManager.HideDragPreview();

        var target =
            GetViewport().GuiGetHoveredControl() == _tableMouseControl ? DropTargetAt(point) : null;
        SetDropTarget(target);
    }

    /// <summary>
    /// The rotation held cards should have when entering or leaving a hand based on the camera rotation and rotation step.
    /// </summary>
    private float HandTurn(ComponentState nearest, bool intoHand)
    {
        float camera = HandCard.CameraYaw(GetViewport().GetCamera3D());
        if (!intoHand)
            return camera;

        float yaw = nearest.Rotation.Y;
        float step = Mathf.DegToRad(
            RecordService.Instance.Single<ProjectGameSettings>().RotationStep()
        );
        return Mathf.Snapped(yaw - camera, step) - yaw;
    }

    private void SetDropTarget(VisualComponentGroup target)
    {
        if (target == _currentDragDropTarget)
            return;
        if (_currentDragDropTarget != null)
            _currentDragDropTarget.IsDropTarget = false;
        _currentDragDropTarget = target;
        if (target != null)
            target.IsDropTarget = true;
    }

    /// <summary>
    /// Gets the drop target under <paramref name="point"/>.
    /// </summary>
    private VisualComponentGroup DropTargetAt(Vector2 point) =>
        Raycast(point, c => c is VisualComponentGroup).Component
            is VisualComponentGroup { CanAcceptDrop: true } group
            ? group
            : null;

    /// <summary>
    /// The components dragged by any player.
    /// </summary>
    private IEnumerable<VisualComponentBase> GetDraggingObjects()
    {
        var R = RecordService.Instance;
        return Nodes(R.Cursors().SelectMany(c => R.Get(ComponentState.ByContainer, c)));
    }

    private IEnumerable<VisualComponentBase> GetNotDraggingObjects() =>
        Nodes(RecordService.Instance.Get(ComponentState.ByContainer, SnowTag.Empty));

    /// <summary>
    /// Drops what <paramref name="held"/> holds on the table at <paramref name="point"/> or into the container there.
    /// </summary>
    private void DropOnTable(HeldDrag held, Vector2 point)
    {
        // The camera may have moved since the mouse did.
        UpdateDrag(held, point);
        // Pointing above the horizon sets nothing down, so the drag is canceled.
        if (_dragPlane.Project(point) is not { } cursor)
            return;

        var dropped = At(cursor, held.Records(RecordService.Instance));
        if (_currentDragDropTarget is { } bag)
            EndDrag(held, bag.DropObjects(dropped));
        else
            EndDrag(held, OnTable(dropped));
    }

    /// <summary>
    /// Drops what <paramref name="held"/> holds into a seat's hand.
    /// </summary>
    public void DropInHand(HeldDrag held, int seat, int index)
    {
        // Puts the cards in the hold over the hand first, if the mouse got there since the last frame.
        UpdateDrag(held, GetViewport().GetMousePosition());
        if (_dragPlane.GetCursorProjection() is not { } cursor)
            return;

        var R = RecordService.Instance;
        var cards = PlayerHandService.Inserted(
            PlayerHandService.GetHand(R, seat),
            held.Cards(R).ToList(),
            index,
            R.HandOf(seat),
            Snowport.Clock.Create()
        );
        var rest = held.Records(R)
            .Where(s => R.Kind(s) != VisualComponentBase.VisualComponentType.Token);
        EndDrag(held, cards.Concat(OnTable(At(cursor, rest))));
    }

    /// <summary>
    /// The held <paramref name="records"/> placed where they are on the table, at their offsets from <paramref name="cursor"/>.
    /// </summary>
    private static List<ComponentState> At(Vector3 cursor, IEnumerable<ComponentState> records) =>
        records.Select(s => s with { Position = cursor + s.PositionAt(0) }).ToList();

    /// <summary>
    /// The records that set <paramref name="dropped"/> down on the table where they are,
    /// in their stacking order, snapped onto a card or deck they're dropped near.
    /// </summary>
    private static ComponentState[] OnTable(List<ComponentState> dropped)
    {
        var ordered = dropped.OrderBy(s => s.ZOrder).ToList();
        var (snapX, snapZ) = SnapDelta(ordered);

        var stamp = Snowport.Clock.Create();
        return ordered
            .Select(
                (s, i) =>
                    s with
                    {
                        ContainerRef = SnowTag.Empty,
                        X = s.X + snapX,
                        Z = s.Z + snapZ,
                        ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                    }
            )
            .ToArray();
    }

    /// <summary>
    /// The offset that snapping should apply to the components.
    /// If a candidate VcToken or deck is found, this offset will snap to a stacked position.
    /// If no candidate is found, returns (0, 0).
    /// </summary>
    private static (int X, int Z) SnapDelta(List<ComponentState> dropped)
    {
        var R = RecordService.Instance;
        var targets = R.Get(ComponentState.ByContainer, SnowTag.Empty)
            .Where(s =>
                R.Kind(s)
                    is VisualComponentBase.VisualComponentType.Token
                        or VisualComponentBase.VisualComponentType.Deck
            )
            .Select(s => (s.X, s.Z))
            .ToList();

        foreach (
            var card in dropped.Where(s =>
                R.Kind(s) == VisualComponentBase.VisualComponentType.Token
            )
        )
        {
            var from = (card.X, card.Z);
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

    #endregion

    #region Drag-Box Selection
    /// <summary>
    /// Starts a box selection from <paramref name="from"/>, in viewport coordinates.
    /// </summary>
    private void StartDragSelection(Vector2 from)
    {
        CursorMode = CursorMode.DragSelect;
        _selectionRectangle.StartDragSelect(from);
    }

    /// <summary>
    /// Selects what the selection box covers in one event.
    /// </summary>
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

        // The gesture went with the table, so there's nothing to cancel.
        _held = null;
        AbandonMode();
        _hover = default;
        _stackingUpdateRequired = 0;
    }

    /// <summary>
    /// Ends the current mode without committing it, back to <see cref="CursorMode.Normal"/>.
    /// </summary>
    private void AbandonMode()
    {
        switch (CursorMode)
        {
            case CursorMode.Drag:
                // Ending Godot's drag runs CleanupDrag, which cancels the gesture.
                GetViewport().GuiCancelDrag();
                break;
            case CursorMode.DragSelect:
                _selectionRectangle.StopDragSelect();
                foreach (var c in ComponentNodes.OfType<VisualComponentBase>())
                    c.PreviewSelected = null;
                break;
            case CursorMode.Spawn:
                ExitSpawnMode();
                break;
        }
        CursorMode = CursorMode.Normal;
    }

    /// <summary>
    /// Moves what each player holds with their cursor.
    /// </summary>
    private void ProcessActiveDrags()
    {
        var cursors = PresenceSynchronizer.Instance;
        if (cursors == null)
            return;

        foreach (var group in GetDraggingObjects().GroupBy(c => c.State.ContainerRef))
        {
            if (cursors.CursorOf(group.Key) is not { } cursor)
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

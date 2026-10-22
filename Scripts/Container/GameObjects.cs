using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;

namespace Lizzie;

public partial class GameObjects : Node3D, ICommandView
{
    [Export]
    private DragPlane _dragPlane;

    [Export]
    private DragSelectRectangle _selectionRectangle;

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

    // set when a component changes, and handled once the frame's updates are in, before it's drawn
    private bool _tableChanged;

    private GameController _gameController;

    /// <summary>
    /// The stage the local player picked to show, or <see cref="SnowTag.Empty"/> for the game.
    /// </summary>
    public WatchableValue<SnowTag> PickedStage { get; } = new();

    /// <summary>
    /// Gets the stage to show locally.
    /// </summary>
    public SnowTag ShownStage(IAtomReader R) =>
        R.Get(PickedStage) is var picked && Stage.Reachable(R, picked)
            ? picked
            : R.Get(CurrentGame.Only).StageRef;

    /// <summary>
    /// Gets the list of stages shown at the top of the app.
    /// </summary>
    public List<Stage> PickableStages(IAtomReader R)
    {
        var stages = Stage.Chain(R, R.Get(CurrentGame.Only).StageRef).Reverse().ToList();
        var shown = ShownStage(R);
        return stages.Any(s => s.Id == shown)
            ? stages
            : [.. Stage.Chain(R, shown).Reverse(), .. stages.TakeLast(1)];
    }

    /// <summary>
    /// Holds the nodes for the shown stage's components, and any spawn previews.
    /// </summary>
    private Table _table;

    /// <summary>
    /// The shown stage's components.
    /// </summary>
    private IReadOnlyList<VisualComponentBase> ComponentNodes => _table.Nodes;

    public CursorMode CursorMode { get; private set; }

    // The local player's drag, from its pickup until Godot's drag ends, or null.
    private HeldDrag _held;

    /// <summary>
    /// The undo group of the local player's drag, which commands on the table join, or <see cref="SnowportId.Empty"/>.
    /// </summary>
    public SnowportId Gesture => _held?.Group ?? SnowportId.Empty;

    /// <summary>
    /// Starts a drag of the atoms in <paramref name="pickup"/>,
    /// which are in stacking order and whose positions are their offsets from the pointer.
    /// The pickup also writes <paramref name="alongside"/>, so a cancel takes them back too.
    /// </summary>
    private void StartDrag(IEnumerable<ComponentPosition> pickup, IEnumerable<Atom> alongside)
    {
        var R = AtomService.Instance;
        bool overHand = HandRow.Under(GetViewport()) != null;
        var holder = overHand ? R.LocalHandHold() : R.LocalCursor();
        var stamp = SnowportId.Create();
        var atoms = pickup
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
        if (atoms.Length == 0 || holder == SnowTag.Empty)
            return;

        _held = new HeldDrag
        {
            Group = R.Open(DragCommands.Pickup, atoms.Concat(alongside)),
            Components = atoms.Select(ComponentPosition.ById.Of).ToArray(),
        };
        CursorMode = CursorMode.Drag;
        _heldOverHand = overHand;
    }

    /// <summary>
    /// What a press on <paramref name="pressed"/> picks up, in stacking order.
    /// </summary>
    private List<VisualComponentBase> PickedUp(VisualComponentBase pressed)
    {
        // Its atom may have been unset this frame, before the table removes it.
        if (pressed.State is not { } state)
            return [];
        var place = state.ContainerRef;
        return GetSelection()
            .Select(GetComponent)
            .Where(o => o?.State?.ContainerRef == place)
            .OrderBy(o => o.State.ZOrder)
            .ToList();
    }

    /// <summary>
    /// Ends a drag with the atoms in <paramref name="drop"/>.
    /// </summary>
    private static void EndDrag(HeldDrag held, IEnumerable<Atom> drop) =>
        AtomService.Instance.Close(held.Group, DragCommands.Drop, drop);

    /// <summary>
    /// Called after the local player's drag ends to perform state cleanup.
    /// </summary>
    private void CleanupDrag()
    {
        if (_held != null)
            AtomService.Instance.CancelIfOpen(_held.Group);
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

        AtomService.Instance.Watch(this, SyncShownStage);
    }

    /// <summary>
    /// Replaces the table when the shown stage changes.
    /// </summary>
    private void SyncShownStage(IWatchReader R)
    {
        if (ShownStage(R) != _table.StageRef)
            ResetTable();
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
        var table = new Table
        {
            Name = "Table",
            TextureFactory = TextureFactory,
            StageRef = ShownStage(AtomService.Instance),
        };
        table.Changed += QueueTableUpdate;
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

    /// <summary>The nodes for the given atoms, skipping any that have none yet.</summary>
    private IEnumerable<VisualComponentBase> Nodes(IEnumerable<ComponentPosition> atoms) =>
        atoms.Select(s => GetComponent(s.Id)).Where(c => c != null);

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
                QueueTableUpdate();
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

    #region ICommandView

    public SelectionPlace? SelectionScope(IAtomReader R) =>
        new SelectionPlace(SelectionPlaces.Table, ShownStage(R));

    public IEnumerable<AtomRef> Hovered() =>
        CursorMode is CursorMode.DragSelect or CursorMode.Spawn
        || GetHoveredObject() is not { } hovered
        || hovered.State == null
            ? []
            : [ComponentPosition.ById.Ref(hovered.Key)];

    public IReadOnlyList<Command> Commands => [DuplicateComponent, ZoomToComponent];

    public bool UndoScope(Atom atom)
    {
        var shown = ShownStage(AtomService.Instance);
        return atom switch
        {
            ComponentPosition s => s.Stage == shown,
            Selection s => s.Place == new SelectionPlace(SelectionPlaces.Table, shown),
            _ => false,
        };
    }

    private static readonly Command DuplicateComponent = new AtomCommand<ComponentPosition>
    {
        Name = new("table.duplicate"),
        Icon = CommandIcons.Duplicate,
        Caption = "Duplicate Component",
        Keys = [Shortcuts.Ctrl(Key.D)],
        Count = TargetCount.One,
        SideEffects = (cs, _) =>
        {
            var variant = AtomService.Instance.GetVariant(cs[0]);
            EventBus.Instance.Publish(
                new SpawnPrototypeEvent { PrototypeRef = variant.PrototypeRef, Item = variant.Item }
            );
        },
    };

    private static readonly Command ZoomToComponent = new AtomCommand<ComponentPosition>
    {
        Name = new("table.zoom"),
        Icon = CommandIcons.Zoom,
        Caption = "Zoom to Component",
        Keys = [Shortcuts.Key(Key.Z)],
        Count = TargetCount.One,
        AppliesTo = (R, c) => !R.IsContained(c) || R.IsInDeck(c),
        SideEffects = (cs, _) =>
        {
            if (ProjectService.Instance.GameObjects.GetComponent(cs[0].Id) is { } node)
                CameraManager.Instance?.ZoomTo(node);
        },
    };
    #endregion

    #region Components


    private void AddComponentToScene(VisualComponentBase component)
    {
        if (component.Reference == SnowTag.Empty)
        {
            GD.PrintErr("component somehow lost its SnowTag");
            return;
        }

        _table.AddChild(component);

        QueueTableUpdate();
    }

    private void CreateComponents(IEnumerable<VisualComponentBase> components)
    {
        var atoms = new List<Atom>();
        var stamp = SnowportId.Create();
        int suborder = 0;

        foreach (var component in components)
        {
            var variant = ComponentVariant.Capture(component);
            var self = ComponentPosition.Capture(component, variant) with
            {
                Id = SnowTag.Create(),
                ZOrder = new ZOrder(ZTarget.Top, suborder++, stamp),
            };
            atoms.Add(variant);
            atoms.Add(self);

            // The stack goes above the component, bottom first.
            foreach (var a in component.GetSpawnStack(self))
            {
                atoms.Add(
                    a is ComponentPosition s
                        ? s with
                        {
                            ZOrder = new ZOrder(ZTarget.Top, suborder++, stamp),
                        }
                        : a
                );
            }
        }

        AtomService.Instance.Write(atoms);
    }

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
    /// <summary>The components the local player has selected, read from their selection atom.</summary>
    private HashSet<SnowTag> GetSelection() =>
        AtomService
            .Instance.GetSelection(ComponentPosition.ById, this)
            .Select(k => k.Component)
            .ToHashSet();

    /// <summary>Replaces the local player's selected components.</summary>
    public void SetSelection(IEnumerable<SnowTag> components) =>
        AtomService.Instance.SetSelection(
            ComponentPosition.ById,
            components.Select(ShownKey),
            this
        );

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
                && PointInRect(camera.UnprojectPosition(c.GlobalPosition), area);
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
        var table = Nodes(
                AtomService.Instance.Get(ComponentPosition.ByContainer, ShownKey(SnowTag.Empty))
            )
            .ToArray();
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

    /// <summary>
    /// Restacks the table once the changes made this frame are in, before the frame is drawn.
    /// Components sync at the end of a frame, so a call made while they sync still lands in that frame.
    /// </summary>
    private void QueueTableUpdate()
    {
        if (_tableChanged)
            return;
        _tableChanged = true;
        Callable.From(UpdateTable).CallDeferred();
    }

    private void UpdateTable()
    {
        _tableChanged = false;
        UpdateDragFloors();
        UpdateStackingHeights();
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
            var (pickup, alongside) = ((VisualComponentGroup)hit.Component).DragDraw();
            StartDrag(
                pickup,
                alongside.Concat(
                    AtomService.Instance.NewSelection(
                        ComponentPosition.ById,
                        pickup.Select(ComponentPosition.ById.Of),
                        this
                    )
                )
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
            AtomService.Instance.LocalSeat(),
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
                .Select(o =>
                {
                    var s = ComponentPosition.Of(o);
                    return s with
                    {
                        Position = o.GlobalPosition - startCursor,
                        Rotation = s.Rotation with
                        {
                            Y = s.Rotation.Y + o.GetParent<Node3D>().GlobalRotation.Y,
                        },
                    };
                }),
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
                    var offset = (row.MiddleOf(c.Key) - press) / row.PixelsPerCm;
                    return ComponentPosition.Of(c) with
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
        var R = AtomService.Instance;
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
    private float HandTurn(ComponentPosition nearest, bool intoHand)
    {
        float camera = HandCard.CameraYaw(GetViewport().GetCamera3D());
        if (!intoHand)
            return camera;

        float yaw = nearest.Rotation.Y;
        float step = Mathf.DegToRad(
            AtomService.Instance.Get(ProjectGameSettings.Only).RotationStep()
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
        var R = AtomService.Instance;
        return Nodes(
            R.Cursors().SelectMany(c => R.Get(ComponentPosition.ByContainer, ShownKey(c)))
        );
    }

    /// <summary>
    /// The key of <paramref name="id"/> in the shown stage.
    /// </summary>
    private StageKey ShownKey(SnowTag id) => new(_table.StageRef, id);

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

        var dropped = At(cursor, held.Atoms(AtomService.Instance));
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

        var R = AtomService.Instance;
        var cards = PlayerHandService.Inserted(
            PlayerHandService.GetHand(R, _table.StageRef, seat),
            held.Cards(R).ToList(),
            index,
            R.HandOf(seat),
            SnowportId.Create()
        );
        var rest = held.Atoms(R)
            .Where(s => R.Kind(s) != VisualComponentBase.VisualComponentType.Printed);
        EndDrag(held, cards.Concat(OnTable(At(cursor, rest))));
    }

    /// <summary>
    /// The held <paramref name="atoms"/> placed where they are on the table, at their offsets from <paramref name="cursor"/>.
    /// </summary>
    private static List<ComponentPosition> At(
        Vector3 cursor,
        IEnumerable<ComponentPosition> atoms
    ) => atoms.Select(s => s with { Position = cursor + s.PositionAt(0) }).ToList();

    /// <summary>
    /// The atoms that set <paramref name="dropped"/> down on the table where they are,
    /// in their stacking order, snapped onto a card or deck they're dropped near.
    /// A token that lands exactly on a deck goes into it.
    /// </summary>
    private ComponentPosition[] OnTable(List<ComponentPosition> dropped)
    {
        var R = AtomService.Instance;
        var ordered = dropped.OrderBy(s => s.ZOrder).ToList();
        var (snapX, snapZ) = SnapDelta(ordered);
        var decks = R.Get(ComponentPosition.ByContainer, ShownKey(SnowTag.Empty))
            .Where(s => R.Kind(s) == VisualComponentBase.VisualComponentType.Deck)
            .ToList();

        var stamp = SnowportId.Create();
        return ordered
            .Select(
                (s, i) =>
                {
                    var placed = s with
                    {
                        ContainerRef = SnowTag.Empty,
                        X = s.X + snapX,
                        Z = s.Z + snapZ,
                        ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                    };
                    var deck =
                        R.Kind(s) == VisualComponentBase.VisualComponentType.Printed
                            ? decks
                                .Where(d => d.X == placed.X && d.Z == placed.Z)
                                .MaxBy(d => d.ZOrder)
                            : null;
                    return deck == null
                        ? placed
                        : placed with
                        {
                            ContainerRef = deck.Id,
                            X = 0,
                            Z = 0,
                            Rotation = placed.Rotation with
                            {
                                Y = placed.Rotation.Y - deck.Rotation.Y,
                            },
                        };
                }
            )
            .ToArray();
    }

    /// <summary>
    /// The offset that snapping should apply to the components.
    /// If a candidate VcPrinted or deck is found, this offset will snap to a stacked position.
    /// If no candidate is found, returns (0, 0).
    /// </summary>
    private (int X, int Z) SnapDelta(List<ComponentPosition> dropped)
    {
        var R = AtomService.Instance;
        var targets = R.Get(ComponentPosition.ByContainer, ShownKey(SnowTag.Empty))
            .Where(s =>
                R.Kind(s)
                    is VisualComponentBase.VisualComponentType.Printed
                        or VisualComponentBase.VisualComponentType.Deck
            )
            .Select(s => (s.X, s.Z))
            .ToList();

        foreach (
            var card in dropped.Where(s =>
                R.Kind(s) == VisualComponentBase.VisualComponentType.Printed
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

        // The gesture went with the table, so there's nothing to cancel.
        _held = null;
        AbandonMode();
        _hover = default;
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

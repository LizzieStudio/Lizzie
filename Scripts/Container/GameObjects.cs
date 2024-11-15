using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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

    public CursorMode CursorMode { get; private set; }

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
    }

    // Specific function to handle normal mode mouse event only outside of GUI elements
    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);
        if (CursorMode == CursorMode.Normal && @event is InputEventMouseButton buttonEvent && buttonEvent.Pressed)
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
                    StartDrag(go);
                }
            }
        }
    }

    #region Components
    /*
    public void AddComponent(VisualComponentBase component)
    {
        AddChild(component);
    }
    */
    
    public void AddComponentToScene(VisualComponentBase component)
    {
        component.ZOrder = GetMaxComponentZ() + 1;
        AddChild(component);
        component.AddComponentToObjects += ComponentOnAddComponentToObjects;
        QueueStackingUpdate();
    }

    private void ComponentOnAddComponentToObjects(object sender, VisualComponentEventArgs e)
    {
        AddComponentToScene(e.Component);
    }

    private void DeleteComponents()
    {
        Update update = new();
        foreach (var go in GetSelectedObjects())
        {
            go.Hide();
            var change = new Change
            {
                Component = go,
                Action = Change.ChangeType.Deletion
            };
            update.Add(change);
        }

        if (update.Count > 0)
        {
            UndoService.Instance.Add(update);
        }
        QueueStackingUpdate();
    }
    #endregion

    #region Hover
    public bool IsAnyObjectHovered()
    {
        return GetChildren().Any(n => n is VisualComponentBase { IsHovered: true });
    }

    public VisualComponentBase GetHoveredObject()
    {
        return GetChildren().FirstOrDefault(n => n is VisualComponentBase { IsHovered: true }) as VisualComponentBase;
    }
    #endregion

    #region Selection
    public bool IsAnyObjectSelected()
    {
        return GetChildren().Any(n => n is VisualComponentBase { IsSelected: true });
    }

    public bool IsAnyObjectMouseSelected()
    {
        return GetChildren().Any(n => n is VisualComponentBase { IsMouseSelected: true });
    }

    public VisualComponentBase GetSelectedObject()
    {
        return GetChildren().FirstOrDefault(n => n is VisualComponentBase { IsSelected: true }) as VisualComponentBase;
    }

    public IEnumerable<VisualComponentBase> GetSelectedObjects()
    {
        return GetChildren()
            .Where(n => n is VisualComponentBase { IsSelected: true })
            .Cast<VisualComponentBase>();
    }

    public VisualComponentBase GetMouseSelectedObject()
    {
        return GetChildren().FirstOrDefault(n => n is VisualComponentBase { IsMouseSelected: true }) as VisualComponentBase;
    }

    public void SelectComponents(Rect2 area)
    {
        foreach (var go in GetChildren())
        {
            if (go is VisualComponentBase vcb)
            {
                var screenPos = GetViewport().GetCamera3D().UnprojectPosition(vcb.Position);
                vcb.IsClickSelected = PointInRect(screenPos, area);
            }
        }
    }

	public void DeselectComponents()
	{
        foreach (var go in GetChildren())
        {
            if (go is VisualComponentBase v)
            {
                v.IsClickSelected = false;
            }
        }
    }
    #endregion

    #region Stacking
    private void MoveToTop()
    {
        var go = GetSelectedObject();
        if (go == null) return;

        var curZ = go.ZOrder;
        var maxZ = GetMaxComponentZ();

        //move everything below the selected object one higher
        foreach (var g in GetChildren())
        {
            if (g is VisualComponentBase vcb && vcb.ZOrder > curZ)
            {
                vcb.ZOrder--;
            }
        }

        go.ZOrder = maxZ;
        QueueStackingUpdate();
    }

    private void MoveToBottom()
    {
        var go = GetSelectedObject();
        if (go == null) return;

        var curZ = go.ZOrder;

        //move everything below the selected object one higher
        foreach (var g in GetChildren())
        {
            if (g is VisualComponentBase vcb && vcb.ZOrder < curZ)
            {
                vcb.ZOrder++;
            }
        }

        go.ZOrder = 1;
        QueueStackingUpdate();
    }

    private void UpdateStackingHeights()
    {
        var children = GetChildren();

        //this dictionary keeps track of objects that are below a certain object. The key is the object id 
        //(in the children array), and the list elements are the object ids of the things that are under it.
        Dictionary<int, List<int>> underneath = new();
        for (int i = 0; i < children.Count; i++)
        {
            var ci = children[i] as VisualComponentBase;

            if (ci == null)
            {
                GD.PrintErr($"{children[i].Name} not VCB");
                continue;
            }

            if (ci.ShapeProfiles.Count == 0) continue;

            for (int j = 0; j < children.Count; j++)
            {
                var cj = children[j] as VisualComponentBase;

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

        for (int i = 0; i < children.Count; i++)
        {
            var ci = children[i] as VisualComponentBase;
            if (ci is null) continue;

            float floor = 0;

            if (underneath.TryGetValue(i, out var elements))
            {
                foreach (var o in elements)
                {
                    if (children[o] is VisualComponentBase co) floor += co.YHeight;
                }
            }

            //GD.Print($"New pos for {i}: {floor + (ci.YHeight / 2f)}");
            ci.Position = new Vector3(ci.Position.X, floor + (ci.YHeight / 2f), ci.Position.Z);
        }
    }

    private int GetMaxComponentZ()
    {
        if (GetChildren().Count == 0) return 0;
        
        return GetChildren()
            .Where(c => c is VisualComponentBase)
            .Cast<VisualComponentBase>()
            .Max(vch => vch.ZOrder);
    }

    private void QueueStackingUpdate()
    {
        _stackingUpdateRequired = _stackingUpdateFrames;
    }
    #endregion

    #region Normal Interaction
    private void HandleNormalMode()
    {
        if (Input.IsActionJustPressed("move_to_top")) MoveToTop();
        if (Input.IsActionJustPressed("move_to_bottom")) MoveToBottom();
        if (Input.IsActionJustPressed("component_delete")) DeleteComponents();
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
        ShowComponentPopup?.Invoke(this, 
            new ShowComponentPopupEventArgs(v, vch));
    }

    public event EventHandler<ShowComponentPopupEventArgs> ShowComponentPopup;

    private void HandlePopupMenu() {}
    
    private void EndPopupMenu()
    {
        CursorMode = CursorMode.Normal;
    }
    #endregion

    #region Spawn
    private VisualComponentBase _spawnComponent;

    public void EnterSpawnMode(VisualComponentBase component)
    {
        CursorMode = CursorMode.Spawn;
        _spawnComponent = component;
        _spawnComponent.DimMode(true);
        _spawnComponent.NeverHighlight = true;
        AddComponentToScene(_spawnComponent);
    }
    
    

    private void HandleSpawnMode()
    {
        _spawnComponent.Position = _dragPlane.GetCursorProjection();

        if (Input.IsActionJustPressed("spawn_component"))
        {
            SpawnComponent();
            QueueStackingUpdate();
        }
        else if (Input.IsActionJustPressed("exit_mode"))
        {
            ExitSpawnMode();
            QueueStackingUpdate();
        }
    }

    private void SpawnComponent()
    {
        var newComp = (VisualComponentBase)_spawnComponent.Duplicate();
        newComp.Build(_spawnComponent.Parameters);
        newComp.DimMode(false);
        newComp.NeverHighlight = false;

        var spawnPosition = _dragPlane.GetCursorProjection();
        newComp.Position = new Vector3(spawnPosition.X, newComp.YHeight / 2f, spawnPosition.Z);

        AddComponentToScene(newComp);
    }

    private void ExitSpawnMode()
    {
        _spawnComponent?.QueueFree();
        _spawnComponent = null;
        CursorMode = CursorMode.Normal;
    }
    #endregion

    #region Drag
    private Vector3 _lastDragPosition;
    private Change _dragChange;

    private void StartDrag(VisualComponentBase go)
    {
        CursorMode = CursorMode.Drag;
        StartDragUndo(go);
        _lastDragPosition = _dragPlane.GetCursorProjection();
        foreach (var gameObject in GetSelectedObjects())
        {
            gameObject.IsDragging = true;
        }
    }

    private void HandleDrag()
    {
        if (Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var newDragPosition = _dragPlane.GetCursorProjection();
            var delta = newDragPosition - _lastDragPosition;
            _lastDragPosition = newDragPosition;

            foreach (var go in GetDraggingObjects())
            {
                go.Position += delta;
            }
        }
        else
        {
            EndDrag();
        }
    }
    
    private IEnumerable<VisualComponentBase> GetDraggingObjects()
    {
        foreach (var n in GetChildren())
        {
            if (n is VisualComponentBase { IsDragging: true } p)
            {
                yield return p;
            }
        }
    }

    private void EndDrag()
    {
        foreach (var gameObject in GetSelectedObjects())
        {
            gameObject.IsDragging = false;
        }
        CursorMode = CursorMode.Normal;
        QueueStackingUpdate();
        EndDragUndo();
    }

    private void StartDragUndo(VisualComponentBase go)
    {
        _dragChange = new()
        {
            Component = go,
            Begin = go.Transform
        };
    }

    private void EndDragUndo()
    {
        _dragChange.End = _dragChange.Component.Transform;
        UndoService.Instance.Add(_dragChange);
        _dragChange = null;
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

        return (point.X >= minX && point.X <= maxX
                                && point.Y >= minY && point.Y <= maxY);
    }
    private static bool CheckOverlap(VisualComponentBase comp1, VisualComponentBase comp2)
    {
        Transform2D t1 = new(comp1.Rotation.Y, new Vector2(comp1.Position.X, comp1.Position.Z));
        Transform2D t2 = new(comp2.Rotation.Y, new Vector2(comp2.Position.X, comp2.Position.Z));

        return comp1.ShapeProfiles
            .Any(s1 => comp2.ShapeProfiles
                .Any(s2 => s1.Collide(t1, s2, t2)));

    }
}

public class ShowComponentPopupEventArgs : EventArgs
{
    public ShowComponentPopupEventArgs(Vector2I position, IEnumerable<VisualComponentBase> components)
    {
        Position = position;
        Components = components;
    }
    public Vector2I Position { get; set; }
    public IEnumerable<VisualComponentBase> Components { get; set; }
}

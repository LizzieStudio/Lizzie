using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks.Dataflow;

public partial class SceneController : Node3D
{
	private Pseudo2DCamera _2dCamera;
	private CameraController _3dCamera;
	private Node _gameObjects;
	private ICameraBase _currentCamera;

	private int _stackingUpdateRequired;

	private DragSelectRectangle _dragSelectRectangle;

	[Export] private int _popupOffset = 32;

	private enum CursorMode
	{
		Normal,
		Spawn,
		Drag,
		DragSelect
	}

	private CursorMode _uiMode = CursorMode.Normal;

	public enum SceneMode
	{
		TwoD,
		ThreeDFixed,
		ThreeDPhysics,
		Creator
	}

	public override void _Ready()
	{
		_3dCamera = GetNode<CameraController>("CameraBase");
		_2dCamera = GetNode<Pseudo2DCamera>("Pseudo2DCamera");
		_gameObjects = GetNode<Node>("GameObjects");
		_currentCamera = _2dCamera;
		_dragSelectRectangle = GetNode<DragSelectRectangle>("DragSelectRectangle");
	}


	public override void _Process(double delta)
	{
		return;

		//Code below was used for testing the 'popup' hamburger menu for options instead 
		//of right-click. Leaving here for now in case we go back.
		var obj = GetSelectedObject();
		if (obj == null)
		{
			GetParent<GameController>().HideComponentPopup();
			return;
		}

		var m = GetViewport().GetMousePosition();

		Vector2I v = new Vector2I((int)Math.Floor(m.X), (int)Math.Floor(m.Y));
		GetParent<GameController>().ShowComponentPopup(v);
	}


	private Vector2 UpperLeftCorner(VisualComponentBase obj)
	{
		var aabb = obj.Aabb;

		//float maxY = float.MinValue;
		float minX = float.MaxValue;
		float minY = float.MaxValue;

		for (int i = 0; i < 8; i++)
		{
			var c = _currentCamera.Camera.UnprojectPosition(aabb.GetEndpoint(i));
			minY = Mathf.Min(minY, c.Y);
			minX = Mathf.Min(minX, c.X);
		}

		int minYI = (int)Math.Floor(minY);
		int minXI = (int)Math.Floor(minX);
		GD.Print($"offset: {minX} {minY}");
		return new Vector2I(minXI - _popupOffset, minYI - _popupOffset);
	}

	public override void _PhysicsProcess(double delta)
	{
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

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (_uiMode)
		{
			case CursorMode.Normal:
				HandleNormal(@event);
				break;

			case CursorMode.Spawn:
				HandleSpawnMode(@event);
				break;

			case CursorMode.Drag:
				HandleDrag(@event);
				break;

			case CursorMode.DragSelect:
				HandleDragSelect();
				break;

			default:
				throw new ArgumentOutOfRangeException();
		}


		CheckForCommands();

		//check for zoom
		if (@event.IsActionPressed("zoom_in")) _currentCamera.ZoomIn();
		if (@event.IsActionPressed("zoom_out")) _currentCamera.ZoomOut();

		//process rotations
		_currentCamera.ProcessViewEvent(@event);

		if (@event.IsActionPressed("reset_view")) _currentCamera.ResetView();

		if (@event.IsActionPressed("move_to_top")) MoveToTop();
		if (@event.IsActionPressed("move_to_bottom")) MoveToBottom();

		if (@event.IsActionPressed("exit_mode")) ResetModes();
		base._Input(@event);
	}

	private void ResetModes()
	{
		DeselectClickedComponents();

		_uiMode = CursorMode.Normal;
	}

	private void DeselectClickedComponents()
	{
		foreach (var go in _gameObjects.GetChildren())
		{
			if (go is VisualComponentBase v) v.IsClickSelected = false;
		}
	}

	#region Normal

	private void HandleNormal(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Right && IsObjectSelected())
			{
				var m = GetViewport().GetMousePosition();

				Vector2I v = new Vector2I((int)Math.Floor(m.X), (int)Math.Floor(m.Y));
				GetParent<GameController>().ShowComponentPopup(v);
			}

			if (mb.ButtonIndex == MouseButton.Left)
			{
				var go = GetMouseSelectedObject(); //only start dragging if we are hovering an item
				if (go == null)
				{
					DeselectClickedComponents();
					_uiMode = CursorMode.DragSelect;
					StartDragSelect();
				}
				else
				{
					_uiMode = CursorMode.Drag;
					_currentCamera.StartDrag();
				}
			}
		}
	}

	#endregion


	#region DragSelect

	private void HandleDragSelect()
	{
		if (!Input.IsMouseButtonPressed(MouseButton.Left))
		{
			StopDragSelect();
		}
		else
		{
			ProcessDragSelect();
		}
	}

	private Vector2 _dragSelectStart;

	private void StartDragSelect()
	{
		GD.Print("Start Drag Select");
		_uiMode = CursorMode.DragSelect;
		_dragSelectRectangle.StartDragSelect(_dragSelectStart);
	}

	private void StopDragSelect()
	{
		_uiMode = CursorMode.Normal;
		_dragSelectRectangle.StopDragSelect();
	}

	private void ProcessDragSelect()
	{
		var rect = _dragSelectRectangle.CurRectangle;

		foreach (var go in _gameObjects.GetChildren())
		{
			if (go is VisualComponentBase vcb)
			{
				var screenPos = _currentCamera.Camera.UnprojectPosition(vcb.Position);
				vcb.IsClickSelected = PointInRect(screenPos, rect);
			}
		}
	}

	private bool PointInRect(Vector2 point, Rect2 rect)
	{
		//normalize in case the size is negative
		float minX = Mathf.Min(rect.Position.X, rect.Position.X + rect.Size.X);
		float maxX = Mathf.Max(rect.Position.X, rect.Position.X + rect.Size.X);

		float minY = Mathf.Min(rect.Position.Y, rect.Position.Y + rect.Size.Y);
		float maxY = Mathf.Max(rect.Position.Y, rect.Position.Y + rect.Size.Y);

		return (point.X >= minX && point.X <= maxX
								&& point.Y >= minY && point.Y <= maxY);
	}

	#endregion

	#region Drag

	private void HandleDrag(InputEvent @event)
	{
		var mouseMotion = new Vector2(0, 0);
		if (@event is InputEventMouseMotion mouse)
		{
			mouseMotion = mouse.Relative;
		}

		if (Input.IsMouseButtonPressed(MouseButton.Left))
		{
			_currentCamera.ProcessDrag(mouseMotion);
		}
		else
		{
			_uiMode = CursorMode.Normal;
			QueueStackingUpdate();
			_currentCamera.StopDrag();
		}
	}

	#endregion


	#region Spawn

	private void HandleSpawnMode(InputEvent @event)
	{
		if (Input.IsMouseButtonPressed(MouseButton.Left))
		{
			SpawnComponent();
			QueueStackingUpdate();
		}
		else
		{
			_spawnDebounce = false;
			if (@event.IsActionPressed("exit_mode"))
			{
				ExitSpawnMode();
				QueueStackingUpdate();
			}
		}
	}

	public void EnterSpawnMode(VisualComponentBase component)
	{
		_uiMode = CursorMode.Spawn;
		_spawnComponent = component;
		_spawnComponent.DimMode(true);
		_currentCamera.EnterSpawnMode(component);
		_gameObjects.AddChild(component);
	}

	public void ExitSpawnMode()
	{
		GD.Print("Exit Spawn Mode");
		_currentCamera.ExitSpawnMode();

		_uiMode = CursorMode.Normal;

		if (!IsInstanceValid(_spawnComponent)) return;

		if (_spawnComponent == null) return;

		if (_spawnComponent.IsQueuedForDeletion()) return;

		_spawnComponent?.QueueFree();
		//_spawnComponent = null;
	}

	private bool _spawnDebounce;
	
	private void SpawnComponent()
	{
		if (_spawnComponent == null || _spawnDebounce) return;

		var newComp = (VisualComponentBase)_spawnComponent.Duplicate();
		newComp.Build(_spawnComponent.Parameters);
		newComp.DimMode(false);
		newComp.ZOrder = GetMaxComponentZ() + 1;
		newComp.NeverHighlight = false;
		GD.Print($"New Z: {newComp.ZOrder}");

		var spawnPos = _currentCamera.GetSpawnPos();
		newComp.Position = new Vector3(spawnPos.X, newComp.YHeight / 2f, spawnPos.Z);

		_gameObjects.AddChild(newComp);
		QueueStackingUpdate();

		_spawnDebounce = true;
	}

	#endregion

	#region Commands

	public enum VisualCommand
	{
		ToggleLock,
		Flip,
		ScaleUp,
		ScaleDown,
		RotateCw,
		RotateCcw,
		Delete,
		Duplicate,
		Edit,
		MoveDown,
		MoveToBottom,
		MoveUp,
		MoveToTop
	}

	private void CheckForCommands()
	{
		if (Input.IsActionJustPressed("flip"))
		{
			SendCommandToSelected(VisualCommand.Flip);
		}
	}

	public bool SendCommandToSelected(VisualCommand command)
	{
		bool result = false;

		foreach (var c in SelectedObjects())
		{
			if (c.ProcessCommand(command))
			{
				result = true;
			}
		}

		return result;
	}

	private IEnumerable<VisualComponentBase> SelectedObjects()
	{
		foreach (var go in _gameObjects.GetChildren())
		{
			if (go is VisualComponentBase { IsSelected: true } vcb)
			{
				yield return vcb;
			}
		}
	}

	#endregion


	private void MoveToTop()
	{
		var go = GetSelectedObject();
		if (go == null) return;

		var curZ = go.ZOrder;
		var maxZ = GetMaxComponentZ();

		//move everything below the selected object one higher
		foreach (var g in _gameObjects.GetChildren())
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
		foreach (var g in _gameObjects.GetChildren())
		{
			if (g is VisualComponentBase vcb && vcb.ZOrder < curZ)
			{
				vcb.ZOrder++;
			}
		}

		go.ZOrder = 1;
		QueueStackingUpdate();
	}

	private bool IsObjectMouseSelected()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is VisualComponentBase { IsMouseSelected: true } p)
			{
				return true;
			}
		}


		return false;
	}

	private bool IsObjectSelected()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is VisualComponentBase { IsSelected: true } p)
			{
				return true;
			}
		}

		return false;
	}

	private VisualComponentBase GetSelectedObject()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is VisualComponentBase { IsSelected: true } p)
			{
				return p;
			}
		}

		return null;
	}

	private VisualComponentBase GetMouseSelectedObject()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is VisualComponentBase { IsMouseSelected: true } p)
			{
				return p;
			}
		}

		return null;
	}

	[Export] private int _stackingUpdateFrames = 3; //Test hack to avoid issue with stacking not seeing colliders

	private void QueueStackingUpdate()
	{
		_stackingUpdateRequired = _stackingUpdateFrames;
	}

	private int GetMaxComponentZ()
	{
		var max = 0;

		foreach (var c in _gameObjects.GetChildren())
		{
			if (c is VisualComponentBase vcb)
			{
				max = Math.Max(max, vcb.ZOrder);
			}
		}

		return max;
	}

	public virtual void SetMode(SceneMode mode)
	{
		switch (mode)
		{
			case SceneMode.TwoD:
				_2dCamera.Current = true;
				_currentCamera = _2dCamera;
				break;
			case SceneMode.ThreeDFixed:
				_3dCamera.Current = true;
				_currentCamera = _3dCamera;
				break;
			case SceneMode.ThreeDPhysics:
				break;
			case SceneMode.Creator:
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
		}
	}

	private VisualComponentBase _spawnComponent;


	public void TestFunction()
	{
		//put test function here
	}

	public void UpdateStackingHeights()
	{
		var children = _gameObjects.GetChildren();

		//this dictionary keeps track of objects that are below a certain object. The key is the object id 
		//(in the children array), and the list elements are the object ids of the things that are under it.
		Dictionary<int, List<int>> underneath = new();
		for (int i = 0; i < children.Count; i++)
		{
			var ci = children[i] as VisualComponentBase;

			if (ci.StackingCollider == null)
			{
				GD.PrintErr($"Null StackingCollider on {i}");
				continue;
			}

			for (int j = 0; j < children.Count; j++)
			{
				var cj = children[j] as VisualComponentBase;

				if (cj.StackingCollider != null)
				{
					if (cj.ZOrder < ci.ZOrder && ci.OverlapsArea(cj)) //lower zOrders are below other items
					{
						GD.Print($"Area {i} overlaps Area {j}");
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
				else
				{
					GD.PrintErr($"Null stacking collider on {j}");
				}
			}
		}

		GD.Print("Collision check complete");

		//uncomment the below to get a printout of the Underneath dictionary

		foreach (var r in underneath)
		{
			string s = String.Empty;
			foreach (var q in r.Value)
			{
				s += $"{q} ";
			}

			GD.Print($"{r.Key} is above {s}");
		}


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

			GD.Print($"New pos for {i}: {floor + (ci.YHeight / 2f)}");
			ci.Position = new Vector3(ci.Position.X, floor + (ci.YHeight / 2f), ci.Position.Z);
		}
	}
}

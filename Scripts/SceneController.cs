using Godot;
using System;
using System.Collections.Generic;

public partial class SceneController : Node3D
{
	private Pseudo2DCamera _2dCamera;
	private CameraController _3dCamera;
	private Node _gameObjects;
	private ICameraBase _currentCamera;

	private bool _isDragging;
	private int _stackingUpdateRequired;
	
	public enum SceneMode {TwoD, ThreeDFixed, ThreeDPhysics, Creator}

	public override void _Ready()
	{
		_3dCamera = GetNode<CameraController>("CameraBase");
		_2dCamera = GetNode<Pseudo2DCamera>("Pseudo2DCamera");
		_gameObjects = GetNode<Node>("GameObjects");
		_currentCamera = _2dCamera;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_stackingUpdateRequired > 0)
		{
			UpdateStackingHeights();
			_stackingUpdateRequired--;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		//if we are spawning a component and there's a left-click, create it
		if (_spawnMode)
		{
			if (Input.IsMouseButtonPressed(MouseButton.Left))
			{
				SpawnComponent();
				_stackingUpdateRequired = 2;
			}
			else
			{
				if (@event.IsActionPressed("exit_mode"))
				{
					ExitSpawnMode();
				}
			}
		}
		
		//otherwise deal with dragging
		else
		{
			//only drag if we are current hovering an object
			var go = GetSelectedObject();

			if (go != null)
			{
				HandleDrag(@event);
			}
			
			//if we are highlighting an object, and are not in spawn mode, pass other commands. 
			//Currently just flip
			go?.ProcessCommands();
		}

		//check for zoom
		if (@event.IsActionPressed("zoom_in")) _currentCamera.ZoomIn();
		if (@event.IsActionPressed("zoom_out")) _currentCamera.ZoomOut();
		
		//process rotations
		_currentCamera.ProcessViewEvent(@event);
		
		if (@event.IsActionPressed("reset_view")) _currentCamera.ResetView();
		
		

		base._Input(@event);
	}
	

	private void HandleDrag(InputEvent @event)
	{
		var mouseMotion = new Vector2(0, 0);
		if (@event is InputEventMouseMotion mouse)
		{
			mouseMotion = mouse.Relative;
		}

		if (Input.IsMouseButtonPressed(MouseButton.Left) && !_spawnMode)
		{
			if (_isDragging)
			{
				_currentCamera.ProcessDrag(mouseMotion);
			}
			else
			{
				_isDragging = true;
				_currentCamera.StartDrag();
			}
		}
		else
		{
			if (_isDragging)
			{
				_isDragging = false;
				_stackingUpdateRequired = 2;
				_currentCamera.StopDrag();
			}
		}
	}

	private VisualComponentBase GetSelectedObject()
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
	
	private void SpawnComponent()
	{
		if (_spawnComponent == null) return;

		var newComp = (VisualComponentBase)_spawnComponent.Duplicate();
		newComp.Build(_spawnComponent.Parameters);
		newComp.DimMode(false);
		newComp.ZOrder = GetMaxComponentZ() + 1;
		GD.Print($"New Z: {newComp.ZOrder}");

		var spawnPos = _currentCamera.GetSpawnPos();
		newComp.Position = new Vector3(spawnPos.X, newComp.YHeight/2f, spawnPos.Z);
		
		_gameObjects.AddChild(newComp);	
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

	private bool _spawnMode;
	private VisualComponentBase _spawnComponent;
	
	public void EnterSpawnMode(VisualComponentBase component)
	{
		_spawnMode = true;
		_spawnComponent = component;
		_spawnComponent.DimMode(true);
		_currentCamera.EnterSpawnMode(component);
		_gameObjects.AddChild(component);
	}

	public void ExitSpawnMode()
	{
		GD.Print("Exit Spawn Mode");
		_currentCamera.ExitSpawnMode();

		_spawnMode = false;

		if (!IsInstanceValid(_spawnComponent)) return;
		
		if (_spawnComponent == null) return;
		
		if (_spawnComponent.IsQueuedForDeletion()) return;
		
		_spawnComponent?.QueueFree();
		//_spawnComponent = null;
	}

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

			if (ci.StackingCollider == null) continue;

			for (int j = 0; j < children.Count; j++)
			{
				var cj = children[j] as VisualComponentBase;

				if (cj.StackingCollider == null) continue;

				if (cj.ZOrder < ci.ZOrder && ci.OverlapsArea(cj))	//lower zOrders are below other items
				{
					GD.Print($"Area {i} overlaps Area {j}");
					//add to dictionary
					if (underneath.ContainsKey(i))
					{
						underneath[i].Add(j);
					}
					else
					{
						underneath.Add(i, new List<int>{j});
					}
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
					var co = children[o] as VisualComponentBase;
					if (co != null) floor += co.YHeight;
				}
			}

			ci.Position = new Vector3(ci.Position.X, floor + (ci.YHeight / 2f), ci.Position.Z);
		}
		
	}
}

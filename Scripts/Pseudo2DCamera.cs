using Godot;
using System;
using System.Collections.Generic;

public partial class Pseudo2DCamera : Camera3D
{
	private Transform3D _baseTransform;
	private Vector3 _baseCamPos;
	private float _baseSize;
	
	private float _totYaw = 0;
	private StaticBody3D _dragPlane;
	private Node _gameObjects;

	private Vector2 _mouseStartDragPos;
	private Vector3 _objectStartDragPos;

	private float _tableSize = 100;

	private bool _spawnMode;
	private VisualComponentBase _spawnComponent;

	private bool _stackingUpdateRequired;
	
	[Export] private float ZoomSpeed { get; set; } = 2f;
	[Export] private float YawSpeed { get; set; } = 1;
	[Export] private float PanSpeed { get; set; } = 10;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_baseCamPos = Position;
		_baseSize = Size;
		_dragPlane = GetParent().GetNode<StaticBody3D>("DragPlane");
		_baseTransform = Transform;
		_gameObjects = GetParent().GetNode<Node>("GameObjects");
		_dragPlane = GetParent().GetNode<StaticBody3D>("DragPlane");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_spawnMode)
		{
			_spawnComponent.Visible = true;
			_spawnComponent.Position = ShootRay(GetViewport().GetMousePosition());
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);
		if (_stackingUpdateRequired)
		{
			CollisionTest();
			_stackingUpdateRequired = false;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Current) return;
		
		int pitch = 0;
		int yaw = 0;
		int zoom = 0;

		int rayLength = 1000;

		Vector2 mouseMotion = new Vector2(0, 0);
		Vector2 mousePos = new Vector2(0, 0);

		
		if (_spawnMode && Input.IsMouseButtonPressed(MouseButton.Left))
		{
			var targetPos = ShootRay(GetViewport().GetMousePosition());

			SpawnComponent(targetPos);
		}
		
		if (@event is InputEventMouseMotion mouse)
		{
			mouseMotion = mouse.Relative;
			
			
			if (Input.IsMouseButtonPressed(MouseButton.Right))
			{
				//use the bigger component for rotation
				if (Math.Abs(mouse.Relative.X) > Math.Abs(mouse.Relative.Y))
				{
					_totYaw += (-0.2f * (mouse.Relative.X ) / 100);
				}
				else
				{
					_totYaw += (-0.2f * (mouse.Relative.Y ) / 100);
				}
			}
			
			if (Input.IsMouseButtonPressed(MouseButton.Middle))
			{
				var curGP = Position;
				var pan = new Vector3(-mouse.Relative.X * PanSpeed, 0, -mouse.Relative.Y * PanSpeed);
				Position = curGP + pan * Size / 12;	//slow down the pan when we are zoomed in
			}
			
		}
		
		
		
		
		if (Input.IsMouseButtonPressed(MouseButton.Left) && !_spawnMode)
		{
			if (_isDragging)
			{
				ProcessDrag(mouseMotion);
			}
			else
			{
				StartDrag();
			}
		}
		else
		{
			if (_isDragging) StopDrag();
		}
		



		if (@event is InputEventMouseButton buttons)
		{
			if (!Current) return;
			
			if (buttons.ButtonIndex == MouseButton.WheelUp) zoom--;
			if (buttons.ButtonIndex == MouseButton.WheelDown) zoom++;
			
		}
		
		
		if (@event is InputEventKey ke)
		{
			if (ke.Keycode == Key.Space)
			{
				Transform = _baseTransform;
				Position = _baseCamPos;
				Size = _baseSize;
			}
			if (ke.Keycode == Key.W) pitch++;
			if (ke.Keycode == Key.S) pitch--;

			if (ke.Keycode == Key.D) yaw++;
			if (ke.Keycode == Key.A) yaw--;
			
			if (ke.Keycode == Key.Q) zoom--;
			if (ke.Keycode == Key.E) zoom++;

			if (ke.Keycode == Key.Escape) ExitSpawnMode();

			if (Input.IsActionJustPressed("flip"))
			{
				SendCommand("Flip");
			}
		}
		
		var delta = (float)GetProcessDeltaTime();
		
		_totYaw += (YawSpeed * delta * yaw);
		
		if (Input.IsKeyPressed(Key.Space))
		{
			_totYaw = 0;
		}

		//Rotation = new Vector3(0, _totYaw, 0);

		float z = Size;
		z += zoom * delta * ZoomSpeed;
		z = Mathf.Clamp(z, 2, _tableSize * 1.1f);
		Size = z;

		var transform = Transform;
		transform.Basis = Basis.Identity;
		Transform = transform;

		Rotation = new Vector3(-3.14159f/2f, 0, _totYaw);
	}

	private void SendCommand(string command)
	{
		_selectedObject = GetSelectedObject();
		if (_selectedObject == null) return;

		GD.Print($"Sending {command}");
		_selectedObject.ProcessCommand(command);
	}
	
	private void SpawnComponent(Vector3 spawnPos)
	{
		if (_spawnComponent == null) return;

		var newComp = (VisualComponentBase)_spawnComponent.Duplicate();
		newComp.Build(_spawnComponent.Parameters);
		newComp.DimMode(false);
		newComp.Position = new Vector3(spawnPos.X, 0, spawnPos.Z);
		
		_gameObjects.AddChild(newComp);
	}

	private bool _isDragging = false;
	private void StartDrag()
	{
		_selectedObject = GetSelectedObject();
		if (_selectedObject == null)
		{
			//GD.PrintErr("No object selected");
			return;
		};

		_selectedObject.IsDragging = true;
		//_selectedObject.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
		_isDragging = true;
		
		
		//Get mouse position
		var rc = ShootRay(GetViewport().GetMousePosition());
		_mouseStartDragPos = new Vector2(rc.X, rc.Z);
		_objectStartDragPos = _selectedObject.Position;
		
		/*
		_dragSource = _selectedObject;

		_dragNode.Transform = _selectedObject.Transform;

		//get the minimum Y for the AABB for the object so we can calculate the distance to the bottom edge
		var b = MinYfromAabb(_selectedObject.Aabb);
		var c = MaxYfromAabb(_selectedObject.Aabb);
		_dragHeight = Mathf.Abs(b - c);
		_dragOffset = _dragNode.GlobalPosition.Y - (_dragHeight/2f);

		//move the intersecting plane to the origin of the object. The shadow object position will be moved
		//to the intersection of the mouse cursor ray and this plane.
		_dragPlane.Position = new Vector3(0, _dragNode.Position.Y, 0);

		var n = _selectedObject.DragMesh.Duplicate();
		if (n is MeshInstance3D mi)
		{
			mi.Transparency = 0.3f;
			_dragMesh = mi;
			_dragNode.AddChild(mi);
		}
		*/
	}

	private void StopDrag()
	{
		if (_selectedObject != null)
		{
			_selectedObject.IsDragging = false;
			//_selectedObject.CanSleep = false;
			//_selectedObject.Sleeping = false;
		
/*
			float offSet = _dragOffset;
		
			var minY = MinY(_dragSource, _dragMesh);
			GD.Print($"miny: {minY} offset:{offSet}");
			minY = Mathf.Max(minY, offSet) + _dragHeight/2f;
			
			_selectedObject.Position = new Vector3( _dragNode.Position.X, minY, _dragNode.Position.Z);

			//_selectedObject.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
*/
			_selectedObject = null;
			_isDragging = false;

			/*
			foreach (var n in _gameObjects.GetChildren())
			{
				if (n is Pickable p)
				{
					p.DoJiggle();
				}
			}
			
			foreach (var c in _dragNode.GetChildren())
			{
				c.QueueFree();
			}
			*/

			_stackingUpdateRequired = true;
		}

		
	}

	private void ProcessDrag(Vector2 axis)
	{
		if (_selectedObject == null) return;

		var targetPos = ShootRay(GetViewport().GetMousePosition());

		var deltaPos = new Vector3(targetPos.X - _mouseStartDragPos.X, 0, targetPos.Z - _mouseStartDragPos.Y);

		_selectedObject.Position = _objectStartDragPos + deltaPos;

		/*
		var rotAxis = axis.Rotated(-Rotation.Y);

		var dragSpeed = 0.05f;

		rotAxis *= dragSpeed;

		var targetPos = _dragNode.Position + new Vector3(rotAxis.X,  0, rotAxis.Y);


		targetPos = ShootRay(GetViewport().GetMousePosition());

		//GD.Print(targetPos);

		_dragNode.Position = targetPos;
		*/
	}

	private Vector3 ShootRay(Vector2 position)
	{
		_dragPlane.InputRayPickable = true;
		var from = ProjectRayOrigin(position);
		var to = from + ProjectRayNormal(position) * 500;
		var ray = new PhysicsRayQueryParameters3D();
		ray.From = from;
		ray.To = to;
		ray.CollisionMask = 4;

		var spaceState = GetWorld3D().DirectSpaceState;
		var res = spaceState.IntersectRay(ray);

		_dragPlane.InputRayPickable = false;

		Vector3 o = new Vector3(-99, -99, -99);

		if (res.ContainsKey("position"))
		{
			o = (Vector3)res["position"];
		}

		//GD.Print(o);
		return o;
	}

	private VisualComponentBase _selectedObject;

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

	public void EnterSpawnMode(VisualComponentBase component)
	{
		_spawnMode = true;
		_spawnComponent = component;
		_spawnComponent.DimMode(true);
		_gameObjects.AddChild(component);
	}

	public void ExitSpawnMode()
	{
		GD.Print("Exit Spawn Mode");
		_spawnMode = false;

		if (!IsInstanceValid(_spawnComponent)) return;
		
		if (_spawnComponent == null) return;
		
		if (_spawnComponent.IsQueuedForDeletion()) return;
		
		_spawnComponent?.QueueFree();
		//_spawnComponent = null;
	}

	public void CollisionTest()
	{
		var children = _gameObjects.GetChildren();

		Dictionary<int, List<int>> collDic = new();
		for (int i = 0; i < children.Count - 1; i++)
		{
			var ci = children[i] as VisualComponentBase;

			if (ci.StackingCollider == null) continue;
			
			for (int j = i + 1; j < children.Count; j++)
			{
				var cj = children[j] as VisualComponentBase;

				if (cj.StackingCollider == null) continue;

				if (ci.OverlapsArea(cj))
				{
					GD.Print($"Area {i} overlaps Area {j}");
					//add to dictionary
					if (collDic.ContainsKey(i))
					{
						collDic[i].Add(j);
					}
					else
					{
						collDic.Add(i, new List<int>{j});
					}
				}
			}
		}
		
		GD.Print("Collision check complete");

		/*
		foreach (var r in collDic)
		{
			string s = String.Empty;
			foreach (var q in r.Value)
			{
				s += $"{q} ";
			}
			
			GD.Print($"{r.Key} collides with {s}");
		}
		*/
		
		//loop through all the objects and check the dictionary (which is in Z order) and stack
		for (int i = 0; i < children.Count; i++)
		{
			var ci = children[i] as VisualComponentBase;
			if (ci is null) continue;
			
			float floor = 0;

			if (collDic.ContainsKey(i))
			{
				foreach (var o in collDic[i])
				{
					var co = children[o] as VisualComponentBase;
					if (co != null) floor += co.YHeight;
				}
			}

			ci.Position = new Vector3(ci.Position.X, floor + (ci.YHeight / 2f), ci.Position.Z);
		}
		
	}
	
}

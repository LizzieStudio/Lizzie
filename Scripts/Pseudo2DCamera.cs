using Godot;
using System;

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
	
	[Export] private float ZoomSpeed { get; set; } = 0.2f;
	[Export] private float YawSpeed { get; set; } = 1;
	[Export] private float PanSpeed { get; set; } = 1;
	
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
	}
	
	public override void _Input(InputEvent @event)
	{
		
		int pitch = 0;
		int yaw = 0;
		int zoom = 0;

		int rayLength = 1000;

		Vector2 mouseMotion = new Vector2(0, 0);
		Vector2 mousePos = new Vector2(0, 0);
		
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
		
		
		
		
		if (Input.IsMouseButtonPressed(MouseButton.Left))
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
		z = Mathf.Clamp(z, 2, 40);
		Size = z;

		var transform = Transform;
		transform.Basis = Basis.Identity;
		Transform = transform;

		Rotation = new Vector3(-3.14159f/2f, 0, _totYaw);
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

	private PickableArea _selectedObject;

	private PickableArea GetSelectedObject()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is PickableArea { IsMouseSelected: true } p)
			{
				return p;
			}
		}

		return null;
	}
}

using Godot;
using System;
using System.Diagnostics;

public partial class CameraController : Node3D
{
	[Export] private float PitchSpeed { get; set; } = 1;
	[Export] private float YawSpeed { get; set; } = 1;
	[Export] private float ZoomSpeed { get; set; } = 0.2f;

	[Export] private float PanSpeed { get; set; } = 10;

	private Node _gameObjects;

	private Camera3D _camera;
	
	private Transform3D _baseTransform;
	private Vector3 _baseCamPos;

	private Transform3D _lastTransform;

	private float _totPitch = 0;
	private float _totYaw = 0;

	private Node3D _dragNode;
	private Pickable _dragSource;
	private VisualInstance3D _dragMesh;
	private StaticBody3D _dragPlane;
	private float _dragOffset;		//distance from the Y of the object origin to the bottom edge

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_baseTransform = Transform;
		_camera = GetNode<Camera3D>("Camera3D");
		 _baseCamPos = _camera.Position;
		 _gameObjects = GetParent().GetNode<Node>("GameObjects");
		 _dragNode = GetParent().GetNode<Node3D>("DragNode");
		 _dragPlane = GetParent().GetNode<StaticBody3D>("DragPlane");
	}

	public override void _Input(InputEvent @event)
	{
		int pitch = 0;
		int yaw = 0;
		int zoom = 0;

		int rayLength = 1000;

		Vector2 mouseMotion = new Vector2(0, 0);
		
		if (@event is InputEventMouseMotion mouse)
		{
			mouseMotion = mouse.Relative;
			
			if (Input.IsMouseButtonPressed(MouseButton.Right))
			{
				_totPitch += (-0.2f * mouse.Relative.Y / 100);
				_totYaw += (-0.2f * mouse.Relative.X / 100);
			}
			
			if (Input.IsMouseButtonPressed(MouseButton.Middle))
			{
				var curGP = GlobalPosition;
				GlobalPosition = curGP + new Vector3(-mouse.Relative.X * PanSpeed, 0, -mouse.Relative.Y * PanSpeed);
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
			
			if (buttons.ButtonIndex == MouseButton.WheelUp) zoom--;
			if (buttons.ButtonIndex == MouseButton.WheelDown) zoom++;
			
			
		}
		
		
		if (@event is InputEventKey ke)
		{
			if (ke.Keycode == Key.Space)
			{
				Transform = _baseTransform;
				_camera.Position = _baseCamPos;
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
		
		_totPitch += (PitchSpeed * delta * pitch);
		_totPitch = Mathf.Clamp(_totPitch, -Mathf.Pi / 2, -0.08f);

		if (Input.IsKeyPressed(Key.Space))
		{
			_totYaw = 0;
			_totPitch = -0.08f;
		}

		Rotation = new Vector3(_totPitch, _totYaw, 0);

		float z = _camera.Position.Z;
		z += zoom * delta * ZoomSpeed;
		z = Mathf.Clamp(z, 2, 40);
		_camera.Position = new Vector3(0, 0, z);

		var transform = Transform;
		transform.Basis = Basis.Identity;
		Transform = transform;

		Rotation = new Vector3(_totPitch, _totYaw, 0);
	}

	public override void _PhysicsProcess(double delta)
	{
		
	}
	
	private Vector3 ShootRay(Vector2 position)
	{
		_dragPlane.InputRayPickable = true;
		var from = _camera.ProjectRayOrigin(position);
		var to = from + _camera.ProjectRayNormal(position) * 500;
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

	private Pickable _selectedObject;

	private Pickable GetSelectedObject()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is Pickable { IsMouseSelected: true } p)
			{
				return p;
			}
		}

		return null;
	}

	private bool _isDragging = false;
	private void StartDrag()
	{
		_selectedObject = GetSelectedObject();
		if (_selectedObject == null)
		{
			GD.PrintErr("No object selected");
			return;
		};
		
		_selectedObject.IsDragging = true;
		_selectedObject.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
		//_selectedObject.CanSleep = true;
		//_selectedObject.Sleeping = true;
		_isDragging = true;

		_dragSource = _selectedObject;

		_dragNode.Transform = _selectedObject.Transform;
		
		//get the minimum Y for the AABB for the object so we can calculate the distance to the bottom edge
		var b = MinYfromAabb(_selectedObject.Aabb);
		var c = MaxYfromAabb(_selectedObject.Aabb);
		var height = Mathf.Abs(b - c);
		_dragOffset = _dragNode.GlobalPosition.Y - (height/2f);
		
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
	}

	private void StopDrag()
	{
		if (_selectedObject != null)
		{
			_selectedObject.IsDragging = false;
			//_selectedObject.CanSleep = false;
			//_selectedObject.Sleeping = false;
		
			float offSet = _dragOffset;
		
			var minY = MinY(_dragSource, _dragMesh);

			minY = Mathf.Max(minY, offSet);
			
			_selectedObject.Position = new Vector3( _dragNode.Position.X, minY, _dragNode.Position.Z);

			//_selectedObject.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;

			_selectedObject = null;
			_isDragging = false;

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
		}

		_dragSource = null;
	}

	private void ProcessDrag(Vector2 axis)
	{
		if (_selectedObject == null) return;

		var rotAxis = axis.Rotated(-Rotation.Y);
		
		var dragSpeed = 0.05f;

		rotAxis *= dragSpeed;

		var targetPos = _dragNode.Position + new Vector3(rotAxis.X,  0, rotAxis.Y);


		targetPos = ShootRay(GetViewport().GetMousePosition());
		
		//GD.Print(targetPos);
		
		_dragNode.Position = targetPos;
	}

	private float MinY(Pickable pickable, VisualInstance3D ghost)
	{
		var pickAabb = pickable.Aabb;
		var minY = -100f;

		var targetAabb = ghost.GlobalTransform * ghost.GetAabb();
		
		int i = 0;
		
		GD.Print(_gameObjects.GetChildren().Count);
		
		
		foreach (var n in _gameObjects.GetChildren())
		{
			i++;
			if (n is Pickable p)
			{
				if (p == pickable) continue;
				if (p.Aabb.Intersects(targetAabb))
				{
					minY = Mathf.Max(minY, MaxYfromAabb(p.Aabb));
				}
				
			}
		}

		return minY;
	}

	private float MaxYfromAabb(Aabb aabb)
	{
		float maxY = float.MinValue;
		for (int i = 0; i < 8; i++)
		{
			maxY = Mathf.Max(maxY, aabb.GetEndpoint(i).Y);
		}

		return maxY;
	}
	
	private float MinYfromAabb(Aabb aabb)
	{
		float minY = float.MaxValue;
		for (int i = 0; i < 8; i++)
		{
			minY = Mathf.Min(minY, aabb.GetEndpoint(i).Y);
		}

		return minY;
	}

	private void DumpAabb(Aabb aabb)
	{
		for (int i = 0; i < 8; i++)
		{
			GD.Print(aabb.GetEndpoint(i));
		}
	}
}

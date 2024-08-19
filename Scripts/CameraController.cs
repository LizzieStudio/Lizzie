using Godot;
using System;
using System.Diagnostics;

public partial class CameraController : Node3D, ICameraBase
{
	[Export] private float PitchSpeed { get; set; } = 1;
	[Export] private float YawSpeed { get; set; } = 1;
	[Export] private float ZoomSpeed { get; set; } = 0.2f;

	[Export] private float PanSpeed { get; set; } = 10;

	private float _tableSize = 100;

	private Node _gameObjects;

	private Camera3D _camera;
	
	private Transform3D _baseTransform;
	private Vector3 _baseCamPos;

	private Transform3D _lastTransform;

	private float _totPitch = 0;
	private float _totYaw = 0;

	private Node3D _dragNode;
	private VisualComponentBase _dragSource;
	private VisualInstance3D _dragMesh;
	private StaticBody3D _dragPlane;
	private float _dragOffset;		//distance from the Y of the object origin to the bottom edge

	private float _dragHeight;		//the distance from the center of the object to the bottom edge
	
	private bool _spawnMode;
	private VisualComponentBase _spawnComponent;
	
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

	public override void _Process(double delta)
	{
		if (_spawnMode)
		{
			_spawnComponent.Visible = true;
			_spawnComponent.Position = ShootRay(GetViewport().GetMousePosition());
		}
	}

	public override void _Input(InputEvent @event)
	{
		
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

	private bool _isDragging = false;

	public void StartDrag()
	{
		GD.Print("StartDrag");
		_selectedObject = GetSelectedObject();
		if (_selectedObject == null)
		{
			GD.PrintErr("No object selected");
			return;
		};

		_selectedObject.IsDragging = true;
		//_selectedObject.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
		_isDragging = true;

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
	}

	public void StopDrag()
	{
		if (_selectedObject != null)
		{
			_selectedObject.IsDragging = false;
		
		
			//float offSet = _dragOffset;
		
			//var minY = MinY(_dragSource, _dragMesh);
			//GD.Print($"miny: {minY} offset:{offSet}");
			//minY = Mathf.Max(minY, offSet) + _dragHeight/2f;
			
			_selectedObject.Position = _dragNode.Position;

			_selectedObject = null;
			_isDragging = false;
			
			foreach (var c in _dragNode.GetChildren())
			{
				c.QueueFree();
			}
		}

		_dragSource = null;
	}

	public void ProcessDrag(Vector2 axis)
	{
		if (_selectedObject == null) return;

		GD.Print("ProcessDrag");
		var rotAxis = axis.Rotated(-Rotation.Y);
		
		var dragSpeed = 0.05f;

		rotAxis *= dragSpeed;

		var targetPos = _dragNode.Position + new Vector3(rotAxis.X,  0, rotAxis.Y);


		targetPos = ShootRay(GetViewport().GetMousePosition());
		
		//GD.Print(targetPos);
		
		_dragNode.Position = targetPos;
	}

	public void ProcessViewEvent(InputEvent @event)
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


		if (@event is InputEventMouseButton buttons)
		{
			
			if (buttons.ButtonIndex == MouseButton.WheelUp) zoom--;
			if (buttons.ButtonIndex == MouseButton.WheelDown) zoom++;
		}
		
		
		if (@event is InputEventKey ke)
		{
			if (ke.Keycode == Key.W) pitch++;
			if (ke.Keycode == Key.S) pitch--;

			if (ke.Keycode == Key.D) yaw++;
			if (ke.Keycode == Key.A) yaw--;
			
		}
		
		var delta = (float)GetProcessDeltaTime();
		
		_totYaw += (YawSpeed * delta * yaw);
		
		_totPitch += (PitchSpeed * delta * pitch);
		_totPitch = Mathf.Clamp(_totPitch, -Mathf.Pi / 2, -0.08f);
		
		Rotation = new Vector3(_totPitch, _totYaw, 0);

		var transform = Transform;
		transform.Basis = Basis.Identity;
		Transform = transform;

		Rotation = new Vector3(_totPitch, _totYaw, 0);
	}

	public void ZoomIn()
	{
		UpdateZoom(-1);
	}

	public void ZoomOut()
	{
		UpdateZoom(1);
	}


	private void UpdateZoom(float zoom)
	{
		var delta = (float)GetProcessDeltaTime();
		float z = _camera.Position.Z;
		z += zoom * delta * ZoomSpeed;
		z = Mathf.Clamp(z, 2, _tableSize * 1.1f);
		_camera.Position = new Vector3(0, 0, z);
	}

	public Vector3 GetSpawnPos() => ShootRay(GetViewport().GetMousePosition());

	
	public void EnterSpawnMode(VisualComponentBase component)
	{
		_spawnMode = true;
		_spawnComponent = component;
	}

	public void ExitSpawnMode()
	{
		GD.Print("Exit Spawn Mode");
		_spawnMode = false;
		_spawnComponent = null;
	}

	public void ResetView()
	{
		Transform = _baseTransform;
		_camera.Position = _baseCamPos;
		_totYaw = 0;
		_totPitch = -0.08f;
	}


	private float MinY(VisualComponentBase pickable, VisualInstance3D ghost)
	{
		//TODO This routine does not deal properly if multiple objects are stacked at the drag location
		//since the moving object AABB may not be intersecting with all of them
		//Needs to be changed to project rays in the Y axis to see if boundary points of moving AABB intersect
		//(basically switch to a 2D projection)
		var pickAabb = pickable.Aabb;
		var minY = -100f;

		var targetAabb = ghost.GlobalTransform * ghost.GetAabb();
		
		int i = 0;
		
		GD.Print(_gameObjects.GetChildren().Count);
		
		
		foreach (var n in _gameObjects.GetChildren())
		{
			i++;
			if (n is VisualComponentBase p)
			{
				GD.Print($"Checking {p.Name}");
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

	public bool Current
	{
		get => _camera.Current;
		set => _camera.Current = value;
	}
	

}

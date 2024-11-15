using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Pseudo2DCamera : Camera3D, ICameraBase
{
	private Transform3D _baseTransform;
	private Vector3 _baseCamPos;
	private float _baseSize;
	
	private float _totYaw = 0;
	private StaticBody3D _dragPlane;
	private Node _gameObjects;

	private Vector2 _mouseStartDragPos;
	
	private float _tableSize = 100;

	private bool _spawnMode;
	private VisualComponentBase _spawnComponent;

	
	
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

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Current) return;
	}


	

	private bool _isDragging = false;
	public void StartDrag()
	{
		if (!GetSelectedObjects().Any()) return;

		var o = GetMouseSelectedObject();
		if (o == null) return;
		
		foreach (var v in GetSelectedObjects())
		{
			v.IsDragging = true;
		}
		_isDragging = true;
		
		
		//Get mouse position
		var rc = ShootRay(GetViewport().GetMousePosition());
		_mouseStartDragPos = new Vector2(rc.X, rc.Z);
	}

	
	public void ProcessViewEvent(InputEvent @event)
	{
		int pitch = 0;
		int yaw = 0;
		
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
		
		if (@event is InputEventKey ke)
		{
			if (ke.Keycode == Key.W) pitch++;
			if (ke.Keycode == Key.S) pitch--;

			if (ke.Keycode == Key.D) yaw++;
			if (ke.Keycode == Key.A) yaw--;
		}
		
		var delta = (float)GetProcessDeltaTime();
		
		_totYaw += (YawSpeed * delta * yaw);
		
		var transform = Transform;
		transform.Basis = Basis.Identity;
		Transform = transform;

		Rotation = new Vector3(-3.14159f/2f, 0, _totYaw);
	}

	public void ZoomIn()
	{
		UpdateZoom(-1);
	}

	public void ZoomOut()
	{
		UpdateZoom(1);
	}

	public void ZoomComponent(VisualComponentBase component)
	{
		Position = new Vector3(component.Position.X, Position.Y, component.Position.Z);

		Size = Mathf.Clamp(component.MaxAxisSize * 1.2f, 2, _tableSize * 1.1f);
	}
 
	private void UpdateZoom(float zoom)
	{
		var delta = (float)GetProcessDeltaTime();
		float z = Size;
		z += zoom * delta * ZoomSpeed;
		z = Mathf.Clamp(z, 2, _tableSize * 1.1f);
		Size = z;
	}

	public Vector3 GetSpawnPos() => ShootRay(GetViewport().GetMousePosition());

	public void ResetView()
	{
		Transform = _baseTransform;
		Position = _baseCamPos;
		Size = _baseSize;
		_totYaw = 0;
	}


	public void StopDrag()
	{
		
		foreach(var v in GetSelectedObjects())
		{
			v.IsDragging = false;
		}

		_isDragging = false;
	}

	public void ProcessDrag(Vector2 axis)
	{
		var targetPos = ShootRay(GetViewport().GetMousePosition());

		var deltaPos = new Vector3(targetPos.X - _mouseStartDragPos.X, 0, targetPos.Z - _mouseStartDragPos.Y);

		foreach (var v in GetDraggingObjects())
		{
			v.Position += deltaPos;
		}

		_mouseStartDragPos = new Vector2(targetPos.X, targetPos.Z);	//reset starting point for next move
	}
	
	private IEnumerable<VisualComponentBase> GetDraggingObjects()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is VisualComponentBase { IsDragging: true } p)
			{
				yield return p;
			}
		}
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

	//private VisualComponentBase _selectedObject;

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

	private IEnumerable<VisualComponentBase> GetSelectedObjects()
	{
		foreach (var n in _gameObjects.GetChildren())
		{
			if (n is VisualComponentBase { IsSelected: true } p)
			{
				yield return p;
			}
		}
	}

	public void EnterSpawnMode(VisualComponentBase component)
	{
		_spawnMode = true;
		_spawnComponent = component;
		_spawnComponent.NeverHighlight = true;
	}

	public void ExitSpawnMode()
	{
		GD.Print("Exit Spawn Mode");
		_spawnMode = false;
		_spawnComponent = null;
	}

	public Camera3D Camera => this;
}

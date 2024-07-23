using Godot;
using System;

public partial class PickableArea : Area3D
{
VisualInstance3D mainMesh;
	Node3D highlightMesh;

	
	private Vector3 _targetPos;

	private float _jiggleForce = 1f;

	private float _jiggleHeight = 0.01f;
	// Called when the node enters the scene tree for the first time.

	[Export] private float _highlightScale = 1.1f;
	private float _curScale = 1;
	
	public override void _Ready()
	{
		_curScale = 1;
		mainMesh = GetNode<VisualInstance3D>("ObjectMesh");
		highlightMesh = GetNode<Node3D>("HighlightMesh");
		highlightMesh.Visible = false;
		IsMouseSelected = false;

		this.MouseEntered += _on_mouse_entered;
		this.MouseExited += _on_mouse_exited;

		//this.CanSleep = false;
	}


	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_jiggle)
		{
			GlobalPosition += new Vector3(0, _jiggleHeight, 0);
			_jiggle = false;
		}
	}

	public void DoJiggle()
	{
		_jiggle = true;
	}

	private bool _jiggle;

	[Export]
	public string Name;

	/*
	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		return;
		if (_jiggle)
		{
			state.ApplyImpulse(new Vector3(0, _jiggleForce,0));
			GD.Print($"Jiggling {Name}");
			_jiggle = false;
		}
		base._IntegrateForces(state);
	}
	*/
	
	public override void _Input(InputEvent @event)
	{
		/*
		if (@event is InputEventMouseButton btn)
		{
			if (btn.ButtonIndex == MouseButton.Left && btn.Pressed && IsSelected)
			{
				IsDragging = true;
			}
			
			else
			{
				IsDragging = false;
			}
		}

		if (@event is InputEventMouseMotion mouse && IsDragging)
		{
			var curPos = GlobalPosition;
			var dragSpeed = 0.05f;
			_targetPos = new Vector3(mouse.Relative.X, 0, mouse.Relative.Y) * dragSpeed;
			GlobalPosition += _targetPos;
		}
		*/
		
		base._Input(@event);
	}

	private bool _isSelected;

	public bool IsMouseSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;

			if (_isSelected)
			{
				Scale *= _highlightScale;
				_curScale = _highlightScale;
			}
			else
			{
				Scale /= _curScale;
				_curScale = 1;
			}
			
			//highlightMesh.Visible = value;
			//mainMesh.Visible = !value;
		}
	}

	public Aabb Aabb => mainMesh.GlobalTransform * mainMesh.GetAabb();

	private void _on_mouse_entered()
	{
		IsMouseSelected = true;
	}

	private void _on_mouse_exited()
	{
		if (!IsDragging) IsMouseSelected = false;
	}

	private bool _isDragging;

	public VisualInstance3D DragMesh => mainMesh;
	
	public bool IsDragging
	{
		get => _isDragging;
		set
		{
			if (_isDragging == value) return;
			_isDragging = value;
			if (!value)
			{
				IsMouseSelected = false;
			}
		}
	}
}

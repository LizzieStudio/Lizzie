using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Container = System.ComponentModel.Container;


public abstract partial class VisualComponentBase : Area3D
{
	public enum VisualComponentType
	{
		Cube,
		Disc,
		Tile,
		Token,
		Board,
		Card,
		Deck,
		Die,
		Mesh,
		Meeple
	}

	public bool TextureReady { get; set; } 
	
	public virtual VisualComponentType ComponentType { get; set; }
	protected GeometryInstance3D MainMesh;

	private MeshInstance3D _highlightMesh;

	public List<Shape2D> ShapeProfiles { get; set; } = new();

	protected MeshInstance3D HighlightMesh
	{
		get => _highlightMesh;
		set
		{
			_highlightMesh = value;
			if (_highlightMesh != null) UpdateHighlight();
		}
	}

	[Export] private float _highlightScale = 1.1f;
	
	public const int TooltipTime = 1000;
	private float _curScale = 1;

	//original creation parameters
    public Dictionary<string, object> Parameters
    {
        get
        {
			if (ProjectService.Instance.CurrentProject == null) return new();
            if (!ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(PrototypeRef, out var proto))
			{
				return new();
			}

            return proto.Parameters;
                
        }
    }

    public override void _Ready()
	{
		Visible = false;
		_curScale = 1;
		
		IsMouseSelected = false;

		MouseEntered += _on_mouse_entered;
		MouseExited += _on_mouse_exited;
		
		base._Ready();
    }

	
	public void MoveToTargetY(float y)
	{
		var tween = GetTree().CreateTween();

		var newPos = new Vector3(Position.X, y, Position.Z);
		tween.TweenProperty(this, "position", newPos, 0.2f);
	}

	public virtual bool Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
	{
		_textureFactory = textureFactory;
        TextureReady = false;
		
		if (parameters.ContainsKey(nameof(ComponentName)))
		{
			ComponentName = parameters[nameof(ComponentName)].ToString();
		}

		return true;
	}

	/// <summary>
	/// Updates the textures, size, etc, without recreating any child objects.
	/// </summary>
	/// <param name="parameters"></param>
	/// <param name="textureFactory"></param>
	/// <returns></returns>
    public virtual bool Refresh(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        return Build(parameters, textureFactory);
    }

	/// <summary>
	/// Checks the parameter dictionary to make sure that everything required for this
	/// component type is included.
	/// </summary>
	/// <param name="parameters"></param>
	/// <returns>List of error messages. If all OK, return zero-element list</returns>
	public abstract List<string> ValidateParameters(Dictionary<string, object> parameters);

	/// <summary>
	/// Process a Command object
	/// </summary>
	/// <param name="command"></param>
	/// <returns>true if action consumed by object. Else false</returns>


	public virtual CommandResponse ProcessCommand(VisualCommand command)
	{
		if (command == VisualCommand.ToggleLock)
		{
			Locked = !Locked;
			
			return new CommandResponse(true, null);
		}

		if (command == VisualCommand.Delete)
		{
			Visible = false;
			IsMouseSelected = false;
			IsClickSelected = false;
			IsDragging = false;
			IsHovered = false;
			
			return new CommandResponse(true, 
				new Change { Component = this, Action = Change.ChangeType.Deletion });
		}

        if (command == VisualCommand.Refresh)
        {
            Refresh(Parameters, _textureFactory);
			return new CommandResponse(true, null);
        }
		
		
		return new CommandResponse(false, null);
	}



    protected TextureFactory _textureFactory;

	public virtual List<MenuCommand> GetMenuCommands()
	{
		var l = new List<MenuCommand>();
		
		//l.Add(new MenuCommand(VisualCommand.ToggleLock, Locked));
		switch (Layer)
		{
			case LayerType.Normal:
				l.Add(new MenuCommand(VisualCommand.Freeze));
				l.Add(new MenuCommand(VisualCommand.Tuck));
				break;
			case LayerType.Frozen:
				l.Add(new MenuCommand(VisualCommand.Unfreeze));
				break;
			case LayerType.Tucked:
				l.Add(new MenuCommand(VisualCommand.Untuck));
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}
		
		l.Add(new MenuCommand(VisualCommand.Delete));
		l.Add(new MenuCommand(VisualCommand.Refresh));
		l.Add(new MenuCommand(VisualCommand.Duplicate));
		return l;
	}

	public virtual string ComponentName { get; set; }

	public virtual Guid PrototypeRef { get; set; }

	public virtual Guid Reference { get; set; } = new Guid();
	
	/// <summary>
	/// Guid of the parent that created this object. Primarily used for decks for recovering
	/// all the cards
	/// </summary>
	public virtual Guid Parent { get; set; }
	
	public virtual Polygon2D YProjection { get; private set; }

	protected float _yHeight;

	public virtual float YHeight
	{
		get
		{
			if (Visible) return _yHeight;
			return 0;
		}
		protected set => _yHeight = value;
	}

	
	public enum LayerType {Normal, Frozen, Tucked}

	public LayerType Layer { get; set; } = LayerType.Normal;
	
	/// <summary>
	/// Sets the Z-order for stacking. A "0" is the lowest - on the table.
	/// If two items have the same Z-Order (should never happen), then
	/// there is no guarantee which will go first.
	/// </summary>
	public virtual int ZOrder { get; set; }

	/// <summary>
	/// The set of Shape3Ds that define the collision volume. Will be a single Shape3D for most items.
	/// </summary>
	public virtual Shape3D[] Bounds { get; protected set; }

	private bool _isMouseSelected;

	public virtual bool IsMouseSelected
	{
		get => _isMouseSelected;
		set
		{
			if (_isMouseSelected == value) return;

			_isMouseSelected = value;
			
			UpdateHighlight();
		}
	}

	protected bool _locked;
	public virtual bool Locked
	{
		get => Layer == LayerType.Frozen;
		set
		{
			if (_locked != value)
			{
				_locked = value;
				LockChanged();
			}
		}
	}

	protected void LockChanged()
	{
		UpdateHighlight();
		IsClickSelected = false;
	}

	public virtual bool IsHovered { get; protected set; }
	
	private bool _isClickSelected;

	public virtual bool IsClickSelected
	{
		get => _isClickSelected;
		set
		{
			if (_isClickSelected == value) return;

			_isClickSelected = !Locked && value;

			UpdateHighlight();
		}
	}
	
	public bool IsSelected => IsMouseSelected || IsClickSelected;
	
	protected virtual void UpdateHighlight()
	{
		if (HighlightMesh == null) return;

	
		HighlightMesh.Visible = IsSelected && !NeverHighlight && !Locked;
	}

	public Aabb Aabb => MainMesh.GlobalTransform * MainMesh.GetAabb();

	public abstract float MaxAxisSize { get; }
	
	private void _on_mouse_entered()
	{
		IsMouseSelected = true;
		IsHovered = true;
	}

	private void _on_mouse_exited()
	{
		if (!IsDragging)
		{
			IsMouseSelected = false;
			IsHovered = false;
		}
		
		//TODO only call this when necessary
		SetHighlightColor(Colors.White);	//reset in case we were a drag target
	}

	//these two events are used when the component itself is creating / removing 
	//other components. Example: Card being drawn from a deck, token from a tray
	public event EventHandler<VisualComponentEventArgs> AddComponentToObjects;

	protected void OnComponentAdded(VisualComponentBase component)
	{
		AddComponentToObjects?.Invoke(this, new VisualComponentEventArgs(component));
	}
	public event EventHandler<VisualComponentEventArgs> RemoveComponentFromObjects;
	
	
	
	public event EventHandler<VisualComponentEventArgs> ShowToolTip;

	public event EventHandler HideToolTip;

	private bool _neverHighlight = false;

	public bool NeverHighlight
	{
		get => _neverHighlight;
		set
		{
			_neverHighlight = value;
			UpdateHighlight();
		}
	}

	private bool _isDragging;

	public abstract GeometryInstance3D DragMesh { get; }

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

	public virtual bool CanAcceptDrop { get; set; } = false;

	public virtual bool DragOver(IEnumerable<VisualComponentBase> dragObjects)
	{
		if (CanObjectsBeDropped(dragObjects))
		{
			SetHighlightColor(Colors.Yellow);
			return true;
		}

		return false;
	}

	public void DragOverExit()
	{
		SetHighlightColor(Colors.White);
	}

	public virtual bool CanObjectsBeDropped(IEnumerable<VisualComponentBase> dragObjects)
	{
		return true;
	}

	public virtual void DropObjects(IEnumerable<VisualComponentBase> dragObjects)
	{
	}

    public virtual string GetPreviewComponentScene() => string.Empty;

    public virtual void SetColor(Color color)
	{
		var objMesh = GetNode<MeshInstance3D>("ObjectMesh");
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = color;
		objMesh.MaterialOverride = mat;
	}

	public virtual void SetHighlightColor(Color color)
	{
		var mat = _highlightMesh.GetActiveMaterial(0);
		if (mat is ShaderMaterial sm)
		{
			sm.SetShaderParameter("outline_color", color);
		}
	}

	protected ImageTexture LoadTexture(string filename)
	{
		var image = new Image();
		var err = image.Load(filename);
		GD.Print(err);

		if (err == Error.Ok)
		{
			var texture = new ImageTexture();
			texture.SetImage(image);
			return texture;
		}

		return new ImageTexture();
	}

	/// <summary>
	/// Need to override this if this simplistic transparency method doesn't work
	/// </summary>
	/// <param name="enableDim"></param>
	public virtual void DimMode(bool enableDim)
	{
		
		if (enableDim)
		{
			DragMesh.Transparency = 0.5f;
		}
		else
		{
			DragMesh.Transparency = 0;
		}

	
	
	}
}

public class VisualComponentEventArgs : EventArgs
{
	public VisualComponentEventArgs(VisualComponentBase component)
	{
		Component = component;
	}
	public VisualComponentBase Component { get; set; }
}


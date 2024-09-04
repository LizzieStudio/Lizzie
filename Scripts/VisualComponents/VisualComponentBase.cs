using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;


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
		Mesh
	}

	public virtual VisualComponentType ComponentType { get; set; }
	protected VisualInstance3D mainMesh;
	protected MeshInstance3D _highlightMesh;

	[Export] private float _highlightScale = 1.1f;
	
	public const int TooltipTime = 1000;
	private float _curScale = 1;

	//original creation parameters
	public Dictionary<string, object> Parameters { get; set; }

	public override void _Ready()
	{
		Visible = false;
		_curScale = 1;
		mainMesh = GetNode<VisualInstance3D>("ObjectMesh");
		_highlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
		if (_highlightMesh != null) UpdateHighlight();
		IsMouseSelected = false;

		this.MouseEntered += _on_mouse_entered;
		this.MouseExited += _on_mouse_exited;
		
		base._Ready();
	}
	

	public virtual bool Build(Dictionary<string, object> parameters)
	{
		Parameters = parameters;
		if (parameters.ContainsKey(nameof(InstanceName)))
		{
			InstanceName = parameters[nameof(InstanceName)].ToString();
		}

		return true;
	}

	public virtual Area3D StackingCollider { get; set; }

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


	public virtual bool ProcessCommand(SceneController.VisualCommand command)
	{
		return false;
	}

	public virtual string InstanceName { get; set; }
	public virtual Polygon2D YProjection { get; private set; }

	public virtual float YHeight { get; protected set; }

	/// <summary>
	/// Sets the Z-order for stacking. A "0" is the lowest - on the table.
	/// If two items have the same Z-Order (should never happen), then
	/// there is no guarantee which will go first.
	/// </summary>
	public int ZOrder { get; set; }

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

	private bool _isClickSelected;

	public virtual bool IsClickSelected
	{
		get => _isClickSelected;
		set
		{
			if (_isClickSelected == value) return;

			_isClickSelected = value;

			UpdateHighlight();
		}
	}

	public bool IsSelected => IsMouseSelected || IsClickSelected;
	
	protected virtual void UpdateHighlight()
	{
		if (_highlightMesh == null) return;
		
		_highlightMesh.Visible = IsSelected && !NeverHighlight;
	}

	public Aabb Aabb => mainMesh.GlobalTransform * mainMesh.GetAabb();

	private void _on_mouse_entered()
	{
		IsMouseSelected = true;
	}

	private void _on_mouse_exited()
	{
		if (!IsDragging)
		{
			IsMouseSelected = false;
		}
	}

	public event EventHandler<ShowTooltipEventArgs> ShowToolTip;

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

	public virtual VisualInstance3D DragMesh => mainMesh;

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

	public virtual void SetColor(Color color)
	{
		var objMesh = GetNode<MeshInstance3D>("ObjectMesh");
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = color;
		objMesh.MaterialOverride = mat;
	}

	protected ImageTexture LoadTexture(string filename)
	{
		GD.Print(filename);
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
		mainMesh = GetNode<VisualInstance3D>("ObjectMesh");
		GD.Print("Dimming");
		if (mainMesh is MeshInstance3D mi)
		{
			GD.Print("MeshInstance3D");
			if (mi.MaterialOverride is StandardMaterial3D sm)
			{
				GD.Print("StandardMaterial3D");
				if (enableDim)
				{
					sm.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
					var color = sm.AlbedoColor;
					sm.AlbedoColor = new Color(color.R, color.G, color.B, 0.5f);
				}
				else
				{
					sm.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
					var color = sm.AlbedoColor;
					sm.AlbedoColor = new Color(color.R, color.G, color.B, 1f);
				}
			}
		}
	}
}

public class ShowTooltipEventArgs : EventArgs
{
	public VisualComponentBase Component { get; set; }
}

using Godot;
using System;
using System.Collections.Generic;

public partial class VcCard : VisualComponentBase
{
	private MeshInstance3D _backSurface;
	private MeshInstance3D _frontSurface;

	public override void _Ready()
	{
		base._Ready();
		Visible = true;
		ComponentType = VisualComponentType.Card;
		_backSurface = GetNode<MeshInstance3D>("HighlightMesh");
		_frontSurface = GetNode<MeshInstance3D>("ObjectMesh");
		StackingCollider = GetNode<Area3D>("Area3D");
	
	}

	public override void _Process(double delta)
	{
		if (flipInProcess)
		{
			ProcessFlip(delta);
		}
		base._Process(delta);
	}

	public override bool ProcessCommands()
	{
		if (Input.IsActionJustPressed("flip"))
		{
			StartFlip();
			return true;
		}
		
		return false;
	}

	private float _flipRate = 720;	//degrees per second
	private bool _showFace = true;
	private int _rotMult = 1;
	private float _targetZ;
	private bool flipInProcess;
	private void StartFlip()
	{
		flipInProcess = true;
		_showFace = !_showFace;
		_rotMult = _showFace ? -1 : 1;
		_targetZ = _showFace ? 0 : 180;
	}

	private void ProcessFlip(double delta)
	{
		var curZ = RotationDegrees.Z;
		float newZ = curZ + (_flipRate * (float)delta * _rotMult);
		if (_showFace)
		{
			if (newZ < _targetZ)
			{
				newZ = _targetZ;
				flipInProcess = false;
			}
		}
		else
		{
			if (newZ > _targetZ)
			{
				newZ = _targetZ;
				flipInProcess = false;
			}
		}

		RotationDegrees = new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ);
	}

	public override bool Build(Dictionary<string, object> parameters)
	{
		_backSurface = GetNode<MeshInstance3D>("HighlightMesh");
		_frontSurface = GetNode<MeshInstance3D>("ObjectMesh");
	
		base.Build(parameters);

		if (parameters.ContainsKey(nameof(Height)))
		{
			if (parameters[nameof(Height)] is float h)
			{
				if (h <= 0) return false;
				Height = h / 10f;
			}

			if (parameters[nameof(Width)] is float w)
			{
				Width = w / 10f;
			}
		}

		FrontImage = parameters["FrontImage"].ToString();
		BackImage = parameters["BackImage"].ToString();
		
		var tf = LoadTexture(FrontImage);
		
		var mat = new StandardMaterial3D();
		mat.AlbedoTexture = tf;
		_frontSurface.MaterialOverride = mat;
		
		var tb = LoadTexture(BackImage);
		
		var mat2 = new StandardMaterial3D();
		mat2.AlbedoTexture = tb;
		_backSurface.MaterialOverride = mat2;

		YHeight = 0.03f;
		
		//create card
		Scale = new Vector3(Width, Scale.Y, Height);
		
		
		
		return true;
	}

	public override List<string> ValidateParameters(Dictionary<string, object> parameters)
	{
		var ret = new List<string>();

		//must have a name and height. Width/length optional
		if (parameters.ContainsKey(nameof(InstanceName)))
		{
			if (string.IsNullOrEmpty(parameters[nameof(InstanceName)].ToString()))
				ret.Add("Instance Name may not be blank");
		}
		else
		{
			ret.Add("Instance Name not included");
		}

		if (parameters.ContainsKey(nameof(Height)))
		{
			if (parameters[nameof(Height)] is float h)
			{
				if (h <= 0) ret.Add("Height must be > 0");
			}
		}
		else
		{
			ret.Add("Height not included");
		}

		if (parameters.TryGetValue(nameof(Width), out var w))
		{
			if (w is float d)
			{
				if (d <= 0) ret.Add("Diameter must be > 0");
			}
		}
		else
		{
			ret.Add("Diameter not included");
		}

		if (parameters.TryGetValue(nameof(FrontImage), out var parameter))
		{
			if (string.IsNullOrEmpty(parameter.ToString()))
			{
				ret.Add("Front Image must be included");
			}
		}

		return ret;
	}

	private float Height;
	private float Width;
	private string FrontImage;
	private string BackImage;
}

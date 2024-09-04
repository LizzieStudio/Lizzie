using Godot;
using System;
using System.Collections.Generic;
using System.IO;

public partial class VcToken : VisualComponentBase
{
	private MeshInstance3D _backSurface;
	private MeshInstance3D _frontSurface;
	
	public override void _Ready()
	{
		base._Ready();
		Visible = true;
		ComponentType = VisualComponentType.Token;
		_backSurface = GetNode<MeshInstance3D>("BackMesh");
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
	
	public override bool ProcessCommand(SceneController.VisualCommand command)
	{
		if (command == SceneController.VisualCommand.Flip)
		{
			StartFlip();
			return true;
		}
		
		return base.ProcessCommand(command);
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
		_backSurface = GetNode<MeshInstance3D>("BackMesh");
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
			
			if (parameters[nameof(Thickness)] is float t)
			{
				Thickness = t / 10f;
			}
		}

		FrontImage = parameters["FrontImage"].ToString();
		BackImage = parameters["BackImage"].ToString();

		ImageTexture tf;
		
		if (File.Exists(FrontImage)) tf = LoadTexture(FrontImage);

		var vt = new Texture2D();
		if (parameters.ContainsKey("QuickTexture"))
		{
			vt = parameters["QuickTexture"] as ViewportTexture;
		}
		
		var vt2 = new Texture2D();
		if (parameters.ContainsKey("QuickTextureBack"))
		{
			vt2 = parameters["QuickTextureBack"] as Texture2D;
		}
		
		var mat = new StandardMaterial3D();
		//mat.AlbedoTexture = tf;
		mat.AlbedoTexture = vt;
		_frontSurface.MaterialOverride = mat;
		
		
		var mat2 = new StandardMaterial3D();

		if (File.Exists(BackImage))
		{
			var tb = LoadTexture(BackImage);

			mat2.AlbedoTexture = tb;
		}
		else
		{
			//mat2.AlbedoColor = new Color(0, 0, 0);
		}
		
		mat2.AlbedoTexture = vt2;
		
		_backSurface.MaterialOverride = mat2;

		YHeight = Thickness;
		
		Scale = new Vector3(Width, Thickness, Height);
		
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
	private float Thickness;
	private string FrontImage;
	private string BackImage;
}

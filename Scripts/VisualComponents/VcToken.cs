using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public partial class VcToken : VisualComponentFlat
{

	private TokenTextureSubViewport _frontView;
	private TokenTextureSubViewport _backView;

	private MeshInstance3D _sideMesh;
	
	public override void _Ready()
	{
		base._Ready();
		Visible = true;
		ComponentType = VisualComponentType.Token;
		
		HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
		FaceSprite = GetNode<Sprite3D>("FrontSprite");
		BackSprite = GetNode<Sprite3D>("BackSprite");
		_sideMesh = GetNode<MeshInstance3D>("SideMesh");
	}

	public override void _Process(double delta)
	{
		if (_flipInProcess)
		{
			ProcessFlip(delta);
		}
		base._Process(delta);
	}

	public override GeometryInstance3D DragMesh => FaceSprite;
	public override float MaxAxisSize => Math.Max(_height, _width);
	public override CommandResponse ProcessCommand(SceneController.VisualCommand command)
	{
		if (command == SceneController.VisualCommand.Flip)
		{
			return StartFlip();
		}
		
		return base.ProcessCommand(command);
	}
	
	public override List<MenuCommand> GetMenuCommands()
	{
		var l = new List<MenuCommand>();

		foreach (var i in base.GetMenuCommands())
		{
			l.Add(i);
		}

		l.Add(new MenuCommand(SceneController.VisualCommand.Flip));
		
		return l;
	}

	private float _flipRate = 720;	//degrees per second
	private int _rotMult = 1;
	private float _targetZ;
	private bool _flipInProcess;
	private CommandResponse StartFlip()
	{
		_flipInProcess = true;
		ShowFace = !ShowFace;
		_rotMult = ShowFace ? -1 : 1;
		_targetZ = ShowFace ? 0 : 180;

		var c = new Change
		{
			Action = Change.ChangeType.Transform,
			Begin = Transform,
			Component = this
		};

		float rot = (float)Math.PI;

		if (_targetZ == 0) rot *= -1;
		
		c.End = Transform.RotatedLocal(new Vector3(0, 0, 1), rot);

		return new CommandResponse(true, c);
	}

	private void ProcessFlip(double delta)
	{
		var curZ = RotationDegrees.Z;
		float newZ = curZ + (_flipRate * (float)delta * _rotMult);
		if (ShowFace)
		{
			if (newZ < _targetZ)
			{
				newZ = _targetZ;
				_flipInProcess = false;
			}
		}
		else
		{
			if (newZ > _targetZ)
			{
				newZ = _targetZ;
				_flipInProcess = false;
			}
		}

		RotationDegrees = new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ);
	}
	
	public override bool Build(Dictionary<string, object> parameters, SceneController sceneController)
	{
		SceneController = sceneController;
		
		FaceSprite = GetNode<Sprite3D>("FrontSprite");
		BackSprite = GetNode<Sprite3D>("BackSprite");
		_sideMesh = GetNode<MeshInstance3D>("SideMesh");
		
		if (!InitializeParameters(parameters)) return false;

		switch (_mode)
		{
			case 0:
				BuildQuick();
				break;
			
			case 1:
				BuildCustom();
				break;
			
			case 2:
				BuildImport();
				break;
		}
		

		YHeight = _thickness;
		
		Scale = new Vector3(_width, _thickness, _height);

		//Don't show the sidemesh if the thickness is too small
		//_sideMesh.Visible = (_thickness > 0.1);
			
		//adjust the scales for the sprites based on the textures so they don't double adjust
		if (_width > 0 && _height > 0)
		{
			float scale = Math.Max(_width, _height);

			var size = new Vector3(scale / _width, 1, scale / _height);
			FaceSprite.Scale = size;
			BackSprite.Scale = size;
			GD.PrintErr(size);
		}

		var shape = (TokenTextureSubViewport.TokenShape)_shape;

		switch (shape)
		{
			case TokenTextureSubViewport.TokenShape.Square:
				var r = new RectangleShape2D();
				r.Size = new Vector2(_width, _height);
				ShapeProfiles.Add(r);
				break;
			
			case TokenTextureSubViewport.TokenShape.Circle:
				var c = new CircleShape2D();
				c.Radius = _width / 2f;
				ShapeProfiles.Add(c);
				break;
			
			case TokenTextureSubViewport.TokenShape.HexPoint:
				var hp = new ConvexPolygonShape2D();
				hp.Points = CalcHexPointVertices();
				ShapeProfiles.Add(hp);
				break;
			
			case TokenTextureSubViewport.TokenShape.HexFlat:
				var hf = new ConvexPolygonShape2D();
				hf.Points = CalcHexPointVertices();
				ShapeProfiles.Add(hf);
				break;
			
			default:
				throw new ArgumentOutOfRangeException();
		}
		
		
		
		return true;
	}

	private Vector2[] CalcHexPointVertices()
	{
		Vector2[] arr = new Vector2[6];

		var x = (_width/ 4f) * Mathf.Sqrt(3) / 2f;
		var y = (_height / 4f);

		arr[0] = new Vector2(0, y * 2);
		arr[1] = new Vector2(-x, y);
		arr[2] = new Vector2(-x, -y);
		arr[3] = new Vector2(0, -y * 2);
		arr[4] = new Vector2(-x, -y);
		arr[5] = new Vector2(-x, y);

		//enable this to print the hex coordinates 
		/*
		foreach (var p in arr)
		{
			GD.Print(p);
		}
		*/
		
		return arr;
	}

	private Vector2[] CalcHexFlatVertices()
	{
		Vector2[] arr = new Vector2[6];

		var x = (_width / 4f);
		var y = (_height/ 4f) * Mathf.Sqrt(3) / 2f;

		arr[0] = new Vector2(x*2, 0);
		arr[1] = new Vector2(x, y);
		arr[2] = new Vector2(-x, y);
		arr[3] = new Vector2(-x *2, 0);
		arr[4] = new Vector2(-x, -y);
		arr[5] = new Vector2(x, -y);
		
		return arr;
	}
	private void BuildQuick()
	{
		_frontView = GetNode<TokenTextureSubViewport>("FrontViewport");
		_frontView.Ready += () => CreateQuickFrontTexture();
		
		if (_differentBack)
		{
			_backView = GetNode<TokenTextureSubViewport>("BackViewport");
			_backView.Ready += () => CreateQuickBackTexture();
		}
	}

	private void BuildCustom()
	{
		_frontView = GetNode<TokenTextureSubViewport>("FrontViewport");
		_frontView.Ready += CreateCustomFrontTexture;
		
		if (_differentBack)
		{
			_backView = GetNode<TokenTextureSubViewport>("BackViewport");
			_backView.Ready += CreateCustomBackTexture;
		}
		
	}
	
	private void BuildImport(){}

	private void CreateCustomFrontTexture()
	{
		if (!File.Exists(_frontImage)) return;
		
		_frontView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
		_frontView.SetShape((TokenTextureSubViewport.TokenShape) _shape);
		_frontView.SetTexture(LoadTexture(_frontImage));

		var t = _frontView.GetTexture();

		float pixelSize = PixelSize(t.GetSize());
		FaceSprite.PixelSize = pixelSize;
		FaceSprite.Texture = t;
		
		
		if (!_differentBack)
		{
			BackSprite.PixelSize = pixelSize;
			BackSprite.Texture = t;
		}
		
	}
	
	//In all the texture creation routines, we scale the pixel size to 0.95.
	//This is the base size of the front and bottom sprites in the token,
	//and matches the side mesh (the gray punchboard texture
	//The width is 0.95 so the highlight mesh, which is size 1.0, so it still shows.
	
	private void CreateCustomBackTexture()
	{
		if (!File.Exists(_backImage)) return;
		
		_backView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
		_backView.SetShape((TokenTextureSubViewport.TokenShape) _shape);
		var t = _backView.GetTexture();

		float pixelSize = PixelSize(t.GetSize());
		BackSprite.PixelSize = pixelSize;
		_backView.SetTexture(LoadTexture(_backImage));

		BackSprite.Texture = _backView.GetTexture();
		
	}

	private float PixelSize(Vector2 size)
	{
		if (size.X == 0 || size.Y == 0) return 0;

		return 0.95f / Mathf.Max(size.X, size.Y);
	}
	
	private void CreateQuickFrontTexture()
	{
		var textureParameters = new TokenTextureParameters
		{
			BackgroundColor = _frontBgColor,
			Caption = _frontCaption,
			CaptionColor = _frontCaptionColor,
			Shape = (TokenTextureSubViewport.TokenShape)_shape,
			Height = _height,
			Width = _width,
			FontSize = _frontFontSize
		};
		
		var t = _frontView.CreateQuickTexture(textureParameters);
		
		float pixelSize = PixelSize(t.GetSize());
		FaceSprite.PixelSize = pixelSize;
		FaceSprite.Texture = t;


		
		if (!_differentBack)
		{
			BackSprite.PixelSize = pixelSize;
			BackSprite.Texture = t;
		}
	}
	
	private void CreateQuickBackTexture()
	{
		var textureParameters = new TokenTextureParameters
		{
			BackgroundColor = _backBgColor,
			Caption = _backCaption,
			CaptionColor = _backCaptionColor,
			Shape = (TokenTextureSubViewport.TokenShape)_shape,
			Height = _height,
			Width = _width,
			FontSize = _backFontSize
		};

		var t = _backView.CreateQuickTexture(textureParameters);
		var pixelSize = PixelSize(t.GetSize());
		BackSprite.PixelSize = pixelSize;
		BackSprite.Texture = t;
	}

	private bool InitializeParameters(Dictionary<string, object> parameters)
	{
		base.Build(parameters, SceneController);

		if (parameters.ContainsKey("Height"))
		{
			if (parameters["Height"] is float h)
			{
				if (h <= 0) return false;
				_height = h / 10f;
			}

			if (parameters["Width"] is float w)
			{
				_width = w / 10f;
			}
			
			if (parameters["Thickness"] is float t)
			{
				_thickness = t / 10f;
			}
		}

		_frontImage = parameters["FrontImage"].ToString();
		_backImage = parameters["BackImage"].ToString();

		_shape = (int)parameters["Shape"];
		_mode = (int)parameters["Mode"];
		_frontBgColor = (Color)parameters["FrontBgColor"];
		_frontCaption = parameters["FrontCaption"].ToString();
		_frontCaptionColor = (Color)parameters["FrontCaptionColor"];
		_frontFontSize = (int)parameters["FrontFontSize"];
		_differentBack = (bool)parameters["DifferentBack"];
		
		_backBgColor = (Color)parameters["BackBgColor"];
		_backCaption = parameters["BackCaption"].ToString();
		_backCaptionColor = (Color)parameters["BackCaptionColor"];
		_backFontSize = (int)parameters["BackFontSize"];
		
		if (parameters.TryGetValue("Type", out var tokenType))
		{
			_tokenType = (TokenType)tokenType;
		}
		else
		{
			_tokenType = TokenType.Token;	//default
		}
		
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

		if (parameters.TryGetValue(nameof(_height), out var height))
		{
			if (height is float h)
			{
				if (h <= 0) ret.Add("Height must be > 0");
			}
		}
		else
		{
			ret.Add("Height not included");
		}

		if (parameters.TryGetValue(nameof(_width), out var w))
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

		if (parameters.TryGetValue(nameof(_frontImage), out var parameter))
		{
			if (string.IsNullOrEmpty(parameter.ToString()))
			{
				ret.Add("Front Image must be included");
			}
		}

		return ret;
	}
	
	

	private float _height;
	private float _width;
	private float _thickness;
	private string _frontImage;
	private string _backImage;
	private int _shape;
	private int _mode;
	private Color _frontBgColor;
	private string _frontCaption;
	private Color _frontCaptionColor;
	private bool _differentBack;
	private Color _backBgColor;
	private string _backCaption;
	private Color _backCaptionColor;
	private TokenType _tokenType;
	private int _frontFontSize;
	private int _backFontSize;
	
	public enum TokenType {Card, Token, Board}
	
}

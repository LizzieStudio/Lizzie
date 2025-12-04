using Godot;
using System;
using System.Collections.Generic;

public partial class VcDie : VisualComponentBase
{
	[Export] private int _sides;
	[Export] private Vector3[] _sideRotations;

	private MeshInstance3D _mainMesh;

	public override void _Ready()
	{
		base._Ready();
		Visible = true;
		
		_mainMesh = GetNode<MeshInstance3D>("ObjectMesh");
		HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
		
		ComponentType = VisualComponentType.Die;
	}

	private bool _rollInProcess;
	private int _rollTarget;
	private double _rollDuration = 0.5;
	private double _rollTime;

	public override void _Process(double delta)
	{
		if (_rollInProcess)
		{
			_rollTime += delta;
			if (_rollTime > _rollDuration)
			{
				ShowSide(_rollTarget);
				_rollInProcess = false;
			}
			else
			{
				ShowSide((int)(GD.Randi() % _sides +1));
			}
		}
	}

	public override float MaxAxisSize => Scale.X;
	public override GeometryInstance3D DragMesh => _mainMesh;
	
	public override CommandResponse ProcessCommand(VisualCommand command)
	{
		var cr = new CommandResponse(false, null);
		
		switch (command)
		{
			case VisualCommand.ToggleLock:
				break;
			case VisualCommand.Flip:
				break;
			case VisualCommand.ScaleUp:
				break;
			case VisualCommand.ScaleDown:
				break;
			case VisualCommand.RotateCw:
				break;
			case VisualCommand.RotateCcw:
				break;
			case VisualCommand.Delete:
				break;
			case VisualCommand.Duplicate:
				break;
			case VisualCommand.Edit:
				break;
			case VisualCommand.MoveDown:
				break;
			case VisualCommand.MoveToBottom:
				break;
			case VisualCommand.MoveUp:
				break;
			case VisualCommand.MoveToTop:
				break;
			
			case VisualCommand.Num1:
				cr = ShowSide(1);
				break;
			case VisualCommand.Num2:
				cr = ShowSide(2);
				break;
			case VisualCommand.Num3:
				cr = ShowSide(3);
				break;
			case VisualCommand.Num4:
				cr = ShowSide(4);
				break;
			case VisualCommand.Num5:
				cr = ShowSide(5);
				break;
			case VisualCommand.Num6:
				cr = ShowSide(6);
				break;
			case VisualCommand.Num7:
				cr = ShowSide(7);
				break;
			case VisualCommand.Num8:
				cr = ShowSide(8);
				break;
			case VisualCommand.Num9:
				cr = ShowSide(9);
				break;
			case VisualCommand.Num10:
				cr = ShowSide(10);
				break;
			case VisualCommand.Num11:
				cr = ShowSide(11);
				break;
			case VisualCommand.Num12:
				cr = ShowSide(12);
				break;
			case VisualCommand.Num13:
				cr = ShowSide(13);
				break;
			case VisualCommand.Num14:
				cr = ShowSide(14);
				break;
			case VisualCommand.Num15:
				cr = ShowSide(15);
				break;
			case VisualCommand.Num16:
				cr = ShowSide(16);
				break;
			case VisualCommand.Num17:
				cr = ShowSide(17);
				break;
			case VisualCommand.Num18:
				cr = ShowSide(18);
				break;
			case VisualCommand.Num19:
				cr = ShowSide(19);
				break;
			case VisualCommand.Num20:
				cr = ShowSide(20);
				break;
			
			case VisualCommand.Roll:
				cr = Roll();
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(command), command, null);
		}
		
		return cr.Consumed == false ? base.ProcessCommand(command) : cr;
	}
	
	public override List<MenuCommand> GetMenuCommands()
	{
		var l = new List<MenuCommand>();

		foreach (var i in base.GetMenuCommands())
		{
			l.Add(i);
		}

		l.Add(new MenuCommand(VisualCommand.Roll));
		
		return l;
	}

	private CommandResponse Roll()
	{
		_rollTarget = (int)(GD.Randi() % _sides + 1);
		_rollInProcess = true;
		_rollTime = 0;
		
		var c = new Change
		{
			Action = Change.ChangeType.Transform,
			Begin = Transform,
			Component = this
		};
		
		//we are cheating to extract the end Transform from the current object. 
		var oldRotation = Rotation;
		
		Rotation = _sideRotations[_rollTarget - 1] * (3.14159f / 180f);	//convert to radians
		
		c.End = Transform;

		Rotation = oldRotation;		//restore the current rotation;

		return new CommandResponse(true, c);
	}

	private CommandResponse ShowSide(int side)
	{
		if (side > _sideRotations.Length) return new CommandResponse(false, null);

		var c = new Change
		{
			Action = Change.ChangeType.Transform,
			Begin = Transform,
			Component = this
		};

		Rotation = _sideRotations[side - 1] * (3.14159f / 180f);	//convert to radians
		c.End = Transform;

		return new CommandResponse(true, c);
	}

	public override bool Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
	{
		base.Build(parameters, textureFactory);

		_mainMesh = GetNode<MeshInstance3D>("ObjectMesh");

		float size = 0;

		if (parameters.ContainsKey("Size"))
		{
			if (parameters["Size"] is float h)
			{
				if (h <= 0) return false;
				size = h / 10f;
			}
		}

		if (parameters.ContainsKey("Color"))
		{
			if (parameters["Color"] is Color color)
			{
				if (_mainMesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D material)
				{
					material.AlbedoColor = color;
				}
			}
		}

		var sides = Utility.GetParam<string[]>(parameters, "Sides");

		YHeight = size;
		
		Scale = new Vector3(size, size, size);

		var mat = new StandardMaterial3D();

		ImageTexture t = new ImageTexture();
		
		if (sides != null && sides.Length > 0)
		{
			if (sides.Length == 6)
			{
				var tx = D6TextureDefinition(sides);
				
				textureFactory.GenerateTexture(tx, TextureDone);
				return true;
			}

			if (sides.Length == 8)
			{
				var tx = D8TextureDefinition(sides);
				textureFactory.GenerateTexture(tx, TextureDone);
				return true;
			}
			
			mat.AlbedoTexture = t;
		}

		_mainMesh.MaterialOverride = mat;
		
		return true;
	}

	private void TextureDone(ImageTexture texture)
	{
		var mat = new StandardMaterial3D();
		mat.AlbedoTexture = texture;
		_mainMesh.MaterialOverride = mat;
		
		var d = texture.GetImage();
		d.SavePng(@"c:\winwam5\d8.png");
	}


	public override List<string> ValidateParameters(Dictionary<string, object> parameters)
	{
		return new List<string>();
	}

	private TextureFactory.TextureDefinition D6TextureDefinition(string[] sides)
	{
		var font = new SystemFont();

		var tx = new TextureFactory.TextureDefinition
		{
			BackgroundColor = Colors.Yellow,
			Height = 256,
			Width = 256,
			Shape = TextureFactory.TextureShape.Square
		};
				
		tx.Objects.Add(new TextureFactory.TextureObject
		{
			CenterX = 128,
			CenterY=128,
			Font = font,
			Height = 128,
			Width = 128,
			Type= TextureFactory.TextureObjectType.RectangleText,
			RotationDegrees = 0,
			Text = "ABCDEFG",
			TextColor = Colors.Black
		});
		
		tx.Objects.Add(new TextureFactory.TextureObject
		{
			CenterX = 128,
			CenterY=128,
			Font = font,
			Height = 128,
			Width = 128,
			Type= TextureFactory.TextureObjectType.RectangleText,
			RotationDegrees = 45,
			Text = "ABCDEFG",
			TextColor = Colors.Black
		});
		
		tx.Objects.Add(new TextureFactory.TextureObject
		{
			CenterX = 128,
			CenterY=128,
			Font = font,
			Height = 128,
			Width = 128,
			Type= TextureFactory.TextureObjectType.RectangleText,
			RotationDegrees = 90,
			Text = "ABCDEFG",
			TextColor = Colors.Black
		});
		
		tx.Objects.Add(new TextureFactory.TextureObject
		{
			CenterX = 128,
			CenterY=128,
			Font = font,
			Height = 128,
			Width = 128,
			Type= TextureFactory.TextureObjectType.RectangleText,
			RotationDegrees = 135,
			Text = "ABCDEFG",
			TextColor = Colors.Black
		});
		
		/*
		tx.Objects.Add(new TextureFactory.TextureObject
		{
			CenterX = 42,
			CenterY=42,
			Font = font,
			Height = 85,
			Width = 85,
			Type= TextureFactory.TextureObjectType.RectangleText,
			RotationDegrees = 0,
			Text = sides[0],
			TextColor = Colors.Black
		});
				
		tx.Objects.Add(new TextureFactory.TextureObject
		{
			CenterX = 42+85,
			CenterY=42,
			Font = font,
			Height = 85,
			Width = 85,
			Type= TextureFactory.TextureObjectType.RectangleText,
			RotationDegrees = 0,
			Text = sides[1],
			TextColor = Colors.Black
		});
		*/
		
		return tx;
	}
	
	private TextureFactory.TextureDefinition D8TextureDefinition(string[] sides)
	{
		var font = new SystemFont();

		var tx = new TextureFactory.TextureDefinition
		{
			BackgroundColor = Colors.Yellow,
			Height = 256,
			Width = 256,
			Shape = TextureFactory.TextureShape.Square
		};

		var t0 = new TextureFactory.TextureObject
		{
			CenterX = 0,
			CenterY = 55,
			Font = font,
			Height = 110,
			Width = 110,
			Type = TextureFactory.TextureObjectType.TriangleText,
			RotationDegrees = 90,
			Text = sides[0],
			TextColor = Colors.Black
		};

		tx.Objects.Add(t0);
		tx.Objects.Add(DuplicateFace(t0, 0, 165, 90, sides[1]));
		tx.Objects.Add(DuplicateFace(t0, 127, 201, -90, sides[2]));
		tx.Objects.Add(DuplicateFace(t0, 127, 92, -90, sides[3]));
		tx.Objects.Add(DuplicateFace(t0, 127, 55, 90, sides[4]));
		tx.Objects.Add(DuplicateFace(t0, 127, 165, 90, sides[5]));
		tx.Objects.Add(DuplicateFace(t0, 255, 201, -90, sides[6]));
		tx.Objects.Add(DuplicateFace(t0, 255, 92, -90, sides[7]));

		
/*		
		data.Add(new DieFaceData( 0, 55, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(0, 165, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(127, 201, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(127, 92, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(127, 55, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(127, 165, 110, 90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(255, 201, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
		data.Add(new DieFaceData(255, 92, 110, -90, TextureBuilderOptions.FaceShape.EquilateralTriangle));
*/

		return tx;
	}

	private TextureFactory.TextureObject DuplicateFace(TextureFactory.TextureObject obj, int centerX, int centerY, int rotation,
		string text)
	{
		TextureFactory.TextureObject tx = new()
		{
			CenterX = centerX,
			CenterY = centerY,
			Font = obj.Font,
			Height = obj.Height,
			Width = obj.Width,
			Type = obj.Type,
			RotationDegrees = rotation,
			Text = text,
			TextColor = obj.TextColor
		};

		return tx;
	}
}

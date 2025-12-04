using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;

public partial class VcDeck : VisualGroupComponent
{
	private Sprite3D _frontSprite;
	private Sprite3D _backSprite;


	private TokenTextureSubViewport _frontView;
	private TokenTextureSubViewport _backView;

	private VcToken _templateCard;
	private string _templateCardPath = "res://Scenes/VisualComponents/VcToken.tscn";

	public override void _Ready()
	{
		base._Ready();
		Visible = true;
		ComponentType = VisualComponentType.Token;

		HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
		_frontSprite = GetNode<Sprite3D>("FrontSprite");
		_backSprite = GetNode<Sprite3D>("BackSprite");

		CanAcceptDrop = true;
	}

	public override void _Process(double delta)
	{
		if (_flipInProcess)
		{
			ProcessFlip(delta);
		}

		if (_spriteUpdateCountdown > 0)
		{
			_spriteUpdateCountdown--;
			if (_spriteUpdateCountdown == 0) UpdateDeckSprites();
		}


		base._Process(delta);
	}

	public override GeometryInstance3D DragMesh => _frontSprite;

	public override float MaxAxisSize => Math.Max(_height, _width);

	public override CommandResponse ProcessCommand(VisualCommand command)
	{
		var cr = new CommandResponse(false, null);

		switch (command)
		{
			case VisualCommand.ToggleLock:
				break;
			case VisualCommand.Flip:
				cr = StartFlip();
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
				cr = DrawCards(1);
				break;
			case VisualCommand.Num2:
				cr = DrawCards(2);
				break;
			case VisualCommand.Num3:
				cr = DrawCards(3);
				break;
			case VisualCommand.Num4:
				cr = DrawCards(4);
				break;
			case VisualCommand.Num5:
				cr = DrawCards(5);
				break;
			case VisualCommand.Num6:
				cr = DrawCards(6);
				break;
			case VisualCommand.Num7:
				cr = DrawCards(7);
				break;
			case VisualCommand.Num8:
				cr = DrawCards(8);
				break;
			case VisualCommand.Num9:
				cr = DrawCards(9);
				break;
			case VisualCommand.Num10:
				cr = DrawCards(10);
				break;
			case VisualCommand.Num11:
				cr = DrawCards(11);
				break;
			case VisualCommand.Num12:
				cr = DrawCards(12);
				break;
			case VisualCommand.Num13:
				cr = DrawCards(13);
				break;
			case VisualCommand.Num14:
				cr = DrawCards(14);
				break;
			case VisualCommand.Num15:
				cr = DrawCards(15);
				break;
			case VisualCommand.Num16:
				cr = DrawCards(16);
				break;
			case VisualCommand.Num17:
				cr = DrawCards(17);
				break;
			case VisualCommand.Num18:
				cr = DrawCards(18);
				break;
			case VisualCommand.Num19:
				cr = DrawCards(19);
				break;
			case VisualCommand.Num20:
				cr = DrawCards(20);
				break;

			case VisualCommand.Shuffle:
				cr = PerformShuffle();
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(command), command, null);
		}

		return cr.Consumed == false ? base.ProcessCommand(command) : cr;
	}

	private CommandResponse PerformShuffle()
	{
		Shuffle();
		UpdateDeckSprites();
		return new CommandResponse(false, null);
	}

	public override List<MenuCommand> GetMenuCommands()
	{
		var l = new List<MenuCommand>();

		foreach (var i in base.GetMenuCommands())
		{
			l.Add(i);
		}

		l.Add(new MenuCommand(VisualCommand.Flip));
		l.Add(new MenuCommand(VisualCommand.Shuffle));
		return l;
	}

	private float _flipRate = 720; //degrees per second
	private bool _showFace = true;
	private int _rotMult = 1;
	private float _targetZ;
	private bool _flipInProcess;

	private CommandResponse StartFlip()
	{
		_flipInProcess = true;
		_showFace = !_showFace;
		_rotMult = _showFace ? -1 : 1;
		_targetZ = _showFace ? 0 : 180;

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
		if (_showFace)
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

	private CommandResponse DrawCards(int count)
	{
		count = Math.Min(count, Children.Count);

		VisualComponentBase[] cards;
		//draw cards
		if (_showFace)
		{
			cards = DrawFromTop(count);
		}
		else
		{
			cards = DrawFromBottom(count);
			cards = cards.Reverse().ToArray();
		}


		//tween to handle movement
		var cardTween = GetTree().CreateTween();


		//splay
		var basePos = Position;

		for (int i = 0; i < cards.Length; i++)
		{
			if (cards[i] is VisualComponentFlat vcf)
			{
				if (_showFace)
				{
					vcf.ForceBack();
				}
				else
				{
					vcf.ForceFace();
				}
			}

			cards[i].Position = basePos;
			cards[i].Visible = false;

			float deltaX = _width * (1.5f + i);

			cardTween.TweenProperty(cards[i], "visible", true, 0.01);
			cardTween.TweenProperty(cards[i], "position", new Vector3(deltaX, 0, 0), 0.2f).AsRelative();


			cards[i].ZOrder = ZOrder + i + 1;

			OnComponentAdded(cards[i]);
		}

		var c = new Change
		{
			Action = Change.ChangeType.Transform,
			Begin = Transform,
			End = Transform,
			Component = this
		};

		UpdateDeckSprites();

		return new CommandResponse(true, c);
	}

	public override bool Build(System.Collections.Generic.Dictionary<string, object> parameters, TextureFactory textureFactory)
	{
		base.Build(parameters, textureFactory);

		_frontSprite = GetNode<Sprite3D>("FrontSprite");
		_backSprite = GetNode<Sprite3D>("BackSprite");

		_frontView = GetNode<TokenTextureSubViewport>("FrontViewport");
		_backView = GetNode<TokenTextureSubViewport>("BackViewport");

		if (!InitializeParameters(parameters)) return false;

		switch (_mode)
		{
			case VcToken.TokenBuildMode.Quick:
				BuildQuick(parameters, textureFactory);
				break;

			case VcToken.TokenBuildMode.Template:
				BuildTemplate(parameters);
				break;

			case VcToken.TokenBuildMode.Grid:
				BuildGrid(parameters, textureFactory);
				break;
		}

		_thickness = 0.03f * Children.Count;
		YHeight = _thickness;

		Scale = new Vector3(_width, _thickness, _height);

		//adjust the scales for the sprites based on the textures so they don't double adjust
		if (_width > 0 && _height > 0)
		{
			float scale = Math.Max(_width, _height);

			var size = new Vector3(scale / _width, 1, scale / _height);
			_frontSprite.Scale = size;
			_backSprite.Scale = size;
		}

		var shape = (TokenTextureSubViewport.TokenShape)_shape;

		switch (shape)
		{
			case TokenTextureSubViewport.TokenShape.Square:
			case TokenTextureSubViewport.TokenShape.RoundedRect:
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

		_frontView.Ready += RegisterInitializedViews;
		_backView.Ready += RegisterInitializedViews;

		//place all cards below the table so they get rendered;

		int h = (int)Math.Floor(_height * 20);
		int w = (int)Math.Floor(_width * 20);

		UpdateDeckSprites();
		return true;
	}

	private ImageTexture _fs;


	private Vector2[] CalcHexPointVertices()
	{
		Vector2[] arr = new Vector2[6];

		var x = (_width / 4f) * Mathf.Sqrt(3) / 2f;
		var y = (_height / 4f);

		arr[0] = new Vector2(0, y * 2);
		arr[1] = new Vector2(-x, y);
		arr[2] = new Vector2(-x, -y);
		arr[3] = new Vector2(0, -y * 2);
		arr[4] = new Vector2(-x, -y);
		arr[5] = new Vector2(-x, y);

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
		var y = (_height / 4f) * Mathf.Sqrt(3) / 2f;

		arr[0] = new Vector2(x * 2, 0);
		arr[1] = new Vector2(x, y);
		arr[2] = new Vector2(-x, y);
		arr[3] = new Vector2(-x * 2, 0);
		arr[4] = new Vector2(-x, -y);
		arr[5] = new Vector2(x, -y);

		return arr;
	}

	private void BuildQuick(Dictionary<string, object> parameters, TextureFactory textureFactory)
	{
		_quickCardList = Utility.GetParam<List<QuickCardData>>(parameters, "QuickCardData");

		if (_quickCardList == null) _quickCardList = new();

		CreateQuickCards(textureFactory);
	}

	private void BuildTemplate(Dictionary<string, object> parameters)
	{
	}


	private void CreateCustomFrontTexture()
	{
		if (!File.Exists(_frontImage)) return;

		_frontView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
		_frontView.SetShape((TokenTextureSubViewport.TokenShape)_shape);
		_frontView.SetTexture(LoadTexture(_frontImage));

		var t = _frontView.GetTexture();

		float pixelSize = Utility.PixelSize(t.GetSize());
		//GD.PrintErr($"Pixel Size: {pixelSize}");
		_frontSprite.PixelSize = pixelSize;
		_frontSprite.Texture = t;


		if (!_differentBack)
		{
			_backSprite.PixelSize = pixelSize;
			_backSprite.Texture = t;
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
		_backView.SetShape((TokenTextureSubViewport.TokenShape)_shape);
		var t = _backView.GetTexture();

		float pixelSize = Utility.PixelSize(t.GetSize());
		_backSprite.PixelSize = pixelSize;
		_backView.SetTexture(LoadTexture(_backImage));

		_backSprite.Texture = _backView.GetTexture();
	}


	private bool InitializeParameters(System.Collections.Generic.Dictionary<string, object> parameters)
	{
		var h = Utility.GetParam<float>(parameters, "Height");
		if (h <= 0) return false;
		_height = h / 10f;


		var w = Utility.GetParam<float>(parameters, "Width");
		_width = w / 10f;

		_mode = Utility.GetParam<VcToken.TokenBuildMode>(parameters, "Mode");

		var scene = ResourceLoader.Load<PackedScene>(_templateCardPath).Instantiate();

		if (scene is not VcToken token) return false;

		_templateCard = token;

		return true;
	}

	private void CreateQuickCards(TextureFactory textureFactory)
	{
		Clear();

		foreach (var q in _quickCardList)
		{
			var values = Utility.ParseValueRanges(q.Caption);

			foreach (var v in values)
			{
				var c = CreateQuickCard(v, q.BackgroundColor, q.CardBackValue, q.CardBackColor, textureFactory);

				AddChildComponent(c);
			}
		}
	}

	private VcToken CreateQuickCard(string faceCaption, Color faceColor, string backCaption, Color backColor, TextureFactory textureFactory)
	{
		var card = (VcToken)_templateCard.Duplicate();
		var p = new System.Collections.Generic.Dictionary<string, object>();

		p.Add("Height", _height * 10);
		p.Add("Width", _width * 10);
		p.Add("Thickness", 0.1f * 10);
		p.Add("ComponentName", string.Empty); //TODO add card name
		p.Add("FrontImage", string.Empty);
		p.Add("BackImage", string.Empty);
		p.Add("Shape", 0);
		p.Add("Mode", VcToken.TokenBuildMode.Quick);
		p.Add("FrontBgColor", faceColor);
		p.Add("FrontCaption", faceCaption);
		p.Add("FrontCaptionColor", Colors.Black);
		p.Add("DifferentBack", true);
		p.Add("BackBgColor", backColor);
		p.Add("BackCaption", backCaption);
		p.Add("BackCaptionColor", Colors.Black);
		p.Add("FrontFontSize", 72);
		p.Add("BackFontSize", 24);
		card.Build(p, textureFactory);

		card.Parent = Reference;

		return card;
	}

	#region Grid Cards

	private Texture2D _frontMasterSprite;
	private Texture2D _backMasterSprite;
	private int _gridRows;
	private int _gridCols;
	private int _gridCount;

	private void BuildGrid(Dictionary<string, object> parameters, TextureFactory textureFactory)
	{
		//Grid Parameters
		_frontMasterSprite = Utility.GetParam<Texture2D>(parameters, "FrontMasterSprite");
		_backMasterSprite = Utility.GetParam<Texture2D>(parameters, "BackMasterSprite");

		_gridRows = Utility.GetParam<int>(parameters, "GridRows");
		_gridCols = Utility.GetParam<int>(parameters, "GridCols");
		_gridCount = Utility.GetParam<int>(parameters, "GridCount");

		CreateGridCards(textureFactory);
	}

	private void CreateGridCards(TextureFactory textureFactory)
	{
		Clear();

		for (int i = 0; i < _gridCount; i++)
		{
			var c = CreateGridCard(i, textureFactory);
			AddChildComponent(c);
		}
	}

	private VcToken CreateGridCard(int index, TextureFactory textureFactory)
	{
		var card = (VcToken)_templateCard.Duplicate();
		var p = new System.Collections.Generic.Dictionary<string, object>();

		p.Add("Height", _height * 10);
		p.Add("Width", _width * 10);
		p.Add("Thickness", 0.03f * 10);
		p.Add("ComponentName", string.Empty); //TODO add card name

		p.Add("Shape", 0);
		p.Add("Mode", VcToken.TokenBuildMode.Grid);

		p.Add("FrontMasterSprite", _frontMasterSprite);
		p.Add("GridRows", _gridRows);
		p.Add("GridCols", _gridCols);
		p.Add("GridIndex", index);

		p.Add("DifferentBack", false);

		card.Build(p, textureFactory);

		card.Parent = Reference;

		return card;
	}

	#endregion

	public override List<string> ValidateParameters(System.Collections.Generic.Dictionary<string, object> parameters)
	{
		var ret = new List<string>();

		//must have a name and height. Width/length optional
		if (parameters.ContainsKey(nameof(ComponentName)))
		{
			if (string.IsNullOrEmpty(parameters[nameof(ComponentName)].ToString()))
				ret.Add("Instance Name may not be blank");
		}
		else
		{
			ret.Add("Instance Name not included");
		}

		if (parameters.ContainsKey(nameof(_height)))
		{
			if (parameters[nameof(_height)] is float h)
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

	private int _spriteUpdateCountdown;

	private int _viewsInitialized = 0;

	private void RegisterInitializedViews()
	{
		_viewsInitialized++;

		if (_viewsInitialized == 2) UpdateDeckSprites();
	}

	private void UpdateDeckSprites()
	{
		//set the top and bottom sprites. 

		//The top of the deck displays the back of the first card. 
		//The bottom of the deck displays the face of the last card.

		//TODO Handle if there are no cards in the deck?
		if (Children.Count > 0)
		{
			if (Children.First() is VisualComponentFlat vcf)
			{
				_frontSprite.PixelSize = vcf.BackSprite.PixelSize;
				_frontSprite.Texture = vcf.BackTexture;
			}

			if (Children.Last() is VisualComponentFlat vcb)
			{
				_backSprite.PixelSize = vcb.FaceSprite.PixelSize;
				_backSprite.Texture = vcb.FaceTexture;
			}


			/*
			switch (_mode)
				{
					case VcToken.TokenBuildMode.Quick:
						CreateQuickFrontTexture();
						CreateQuickBackTexture();
						break;
					case VcToken.TokenBuildMode.Grid:
						break;
					case VcToken.TokenBuildMode.Template:
						break;
					case VcToken.TokenBuildMode.Nandeck:
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
				*/
		}
	}

	private float _height;
	private float _width;
	private float _thickness;
	private string _frontImage;
	private string _backImage;
	private int _shape;
	private VcToken.TokenBuildMode _mode;
	private Color _frontBgColor;
	private string _frontCaption;
	private Color _frontCaptionColor;
	private bool _differentBack;
	private Color _backBgColor;
	private string _backCaption;
	private Color _backCaptionColor;
	private List<QuickCardData> _quickCardList = new();
	protected override void OnChildrenChanged()
	{
		UpdateDeckSprites();
	}
}

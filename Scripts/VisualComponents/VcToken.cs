using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using Vector2 = Godot.Vector2;

public partial class VcToken : VisualComponentFlat
{
    private TokenTextureSubViewport _frontView;
    private TokenTextureSubViewport _backView;

    private MeshInstance3D _sideMesh;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Token;

        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        //FaceSprite = GetNode<Sprite3D>("FrontSprite");
        //BackSprite = GetNode<Sprite3D>("BackSprite");
        //_sideMesh = GetNode<MeshInstance3D>("SideMesh");
    }

    public override void _Process(double delta)
    {
        if (_flipInProcess)
        {
            ProcessFlip(delta);
        }

        if (_buildRequired)
        {
            BuildToken();
            _buildRequired = false;
        }

        if (_mapFrontTextureRequired)
        {
            MapFrontTexture();
        }

        if (_mapBackTextureRequired)
        {
            MapBackTexture();
        }

        if (!TextureReady)
        {
            TextureReady = _frontTextureGenerated && _backTextureGenerated;
        }

        base._Process(delta);
    }

    public override GeometryInstance3D DragMesh => FaceSprite;
    public override float MaxAxisSize => Math.Max(_height, _width);

    public override CommandResponse ProcessCommand(VisualCommand command)
    {
        if (command == VisualCommand.Flip)
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

        l.Add(new MenuCommand(VisualCommand.Flip));

        return l;
    }

    private float _flipRate = 720; //degrees per second
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
            Component = this,
        };

        float rot = (float)Math.PI;

        if (_targetZ == 0)
            rot *= -1;

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

        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ));
    }

    private bool _firstBuild = true;

    public enum TokenBuildMode
    {
        Quick,
        Custom,
        Grid,
        Template,
        Nandeck,
    }

    private bool _buildRequired = false;

    public override bool Build(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        TextureFactory = textureFactory;
        TempParams = parameters;

        if (!InitializeParameters(parameters, textureFactory))
            return false;

        switch (_mode)
        {
            case TokenBuildMode.Quick:
                BuildQuick(textureFactory);
                break;

            case TokenBuildMode.Custom:
                BuildCustom();
                break;

            case TokenBuildMode.Grid:
                BuildGrid();
                break;

            case TokenBuildMode.Template:
                BuildTemplate(textureFactory);
                break;

            case TokenBuildMode.Nandeck:
                BuildNanDeck();
                break;
        }

        BuildToken();

        return true;
    }

    private void BuildToken()
    {
        if (!IsNodeReady())
        {
            _buildRequired = true;
            return;
        }

        FaceSprite = GetNode<Sprite3D>("FrontSprite");
        BackSprite = GetNode<Sprite3D>("BackSprite");
        _sideMesh = GetNode<MeshInstance3D>("SideMesh");

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
            //GD.PrintErr(size);
        }

        ShapeProfiles.Clear();

        var shape = (TokenTextureSubViewport.TokenShape)_shape;

        switch (shape)
        {
            case TokenTextureSubViewport.TokenShape.Square:
                var r = new RectangleShape2D();
                r.Size = new Vector2(_width, _height);
                ShapeProfiles.Add(new OffsetShape2D(r));
                break;

            case TokenTextureSubViewport.TokenShape.Circle:
                var c = new CircleShape2D();
                c.Radius = _width / 2f;
                ShapeProfiles.Add(new OffsetShape2D(c));
                break;

            case TokenTextureSubViewport.TokenShape.HexPoint:
                var hp = new ConvexPolygonShape2D();
                hp.Points = CalcHexPointVertices();
                ShapeProfiles.Add(new OffsetShape2D(hp));
                break;

            case TokenTextureSubViewport.TokenShape.HexFlat:
                var hf = new ConvexPolygonShape2D();
                hf.Points = CalcHexPointVertices();
                ShapeProfiles.Add(new OffsetShape2D(hf));
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        _firstBuild = false;
    }

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
        var y = (_height / 4f) * Mathf.Sqrt(3) / 2f;

        arr[0] = new Vector2(x * 2, 0);
        arr[1] = new Vector2(x, y);
        arr[2] = new Vector2(-x, y);
        arr[3] = new Vector2(-x * 2, 0);
        arr[4] = new Vector2(-x, -y);
        arr[5] = new Vector2(x, -y);

        return arr;
    }

    private void BuildQuick(TextureFactory textureFactory)
    {
        CreateQuickFrontTexture(textureFactory);
        if (_differentBack)
            CreateQuickBackTexture(textureFactory);
    }

    private void BuildCustom()
    {
        _frontView = GetNode<TokenTextureSubViewport>("FrontViewport");
        if (_firstBuild)
        {
            _frontView.Ready += CreateCustomFrontTexture;
        }
        else
        {
            CreateCustomFrontTexture();
        }

        if (_differentBack)
        {
            _backView = GetNode<TokenTextureSubViewport>("BackViewport");
            if (_firstBuild)
            {
                _backView.Ready += CreateCustomBackTexture;
            }
            else
            {
                CreateCustomBackTexture();
            }
        }
    }

    private void BuildGrid()
    {
        if (_frontMasterSprite is null)
            return;

        if (_gridCols == 0 || _gridRows == 0)
            return;

        var ts = _frontMasterSprite.GetSize();

        var cv = new Vector2(ts.X / _gridCols, ts.Y / _gridRows);
        var pixelSize = PixelSize(cv);

        FaceSprite.PixelSize = pixelSize;
        _frontTextureGenerated = true;

        if (!_differentBack)
        {
            BackSprite.PixelSize = pixelSize;
            _backTextureGenerated = true;
            //BackSprite.Texture = t;
        }

        FaceSprite.Texture = _frontMasterSprite;
        FaceSprite.Hframes = _gridCols;
        FaceSprite.Vframes = _gridRows;
        FaceSprite.Frame = _gridIndex;
    }

    private string _frontTemplateName;
    private string _backTemplateName;
    private string _datasetName;

    private void BuildTemplate(TextureFactory textureFactory)
    {
        _differentBack = true;

        int sH = 256;
        int sW = 256;

        if (_height <= 0 || _width <= 0)
            return;

        if (_height > _width)
        {
            sW = (int)(_width * 256 / _height);
        }
        else
        {
            sH = (int)(_height * 256 / _width);
        }

        if (string.IsNullOrWhiteSpace(_frontTemplateName))
            return;

        var curProj = ProjectService.Instance.CurrentProject;

        var ft = curProj.GetTemplate(_frontTemplateName);
        var bt = curProj.GetTemplate(_backTemplateName);
        var ds = curProj.GetDataset(_datasetName);

        TextureContext context = new TextureContext
        {
            DataSet = ds,
            Dpi = 100,
            ParentSize = new Vector2(250, 350),
            CurrentRowName = DataSetRow,
        };

        _frontTextureGenerated = true;
        _backTextureGenerated = true;

        if (ft is not null)
        {
            var ftd = TemplateEngine.GenerateTextureDefinition(ft, context);
            _frontTextureGenerated = false;
            textureFactory.GenerateTexture(ftd, FinalizeFrontTexture);
        }

        if (bt is not null)
        {
            var btd = TemplateEngine.GenerateTextureDefinition(bt, context);
            _backTextureGenerated = false;
            textureFactory.GenerateTexture(btd, FinalizeBackTexture);
        }
    }

    private bool _frontTextureGenerated;
    private bool _backTextureGenerated;

    private void BuildNanDeck() { }

    private void BuildImport() { }

    private void CreateCustomFrontTexture()
    {
        if (!File.Exists(_frontImage))
            return;

        _frontView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
        _frontView.SetShape((TokenTextureSubViewport.TokenShape)_shape);
        _frontView.SetTexture(LoadTexture(_frontImage));

        _frontTextureGenerated = true;

        var t = _frontView.GetTexture();

        float pixelSize = PixelSize(t.GetSize());
        FaceSprite.PixelSize = pixelSize;
        FaceSprite.Texture = t;

        if (!_differentBack)
        {
            BackSprite.PixelSize = pixelSize;
            BackSprite.Texture = t;
            _backTextureGenerated = true;
        }
    }

    //In all the texture creation routines, we scale the pixel size to 0.95.
    //This is the base size of the front and bottom sprites in the token,
    //and matches the side mesh (the gray punchboard texture
    //The width is 0.95 so the highlight mesh, which is size 1.0, so it still shows.

    private void CreateCustomBackTexture()
    {
        if (!File.Exists(_backImage))
            return;

        _backView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
        _backView.SetShape((TokenTextureSubViewport.TokenShape)_shape);
        var t = _backView.GetTexture();

        float pixelSize = PixelSize(t.GetSize());
        BackSprite.PixelSize = pixelSize;
        _backView.SetTexture(LoadTexture(_backImage));

        BackSprite.Texture = _backView.GetTexture();

        _backTextureGenerated = true;
    }

    private float PixelSize(Vector2 size)
    {
        if (size.X == 0 || size.Y == 0)
            return 0;

        return 0.95f / Mathf.Max(size.X, size.Y);
    }

    private void CreateQuickFrontTexture(TextureFactory textureFactory)
    {
        var td = CreateQuickTextureDefinition(_frontBgColor, _frontField);

        textureFactory.GenerateTexture(td, FinalizeFrontTexture);
    }

    private TextureFactory.TextureDefinition CreateQuickTextureDefinition(
        Color bgColor,
        QuickTextureField qtf
    )
    {
        int sH = 256;
        int sW = 256;

        if (_height <= 0 || _width <= 0)
            return new TextureFactory.TextureDefinition();
        if (_height > _width)
        {
            sW = (int)(_width * 256 / _height);
        }
        else
        {
            sH = (int)(_height * 256 / _width);
        }

        var td = new TextureFactory.TextureDefinition
        {
            BackgroundColor = bgColor,
            Height = sH,
            Width = sW,
        };

        /*
         Square = 0,
        Circle = 1,
        HexPoint = 2,
        HexFlat = 3,
        RoundedRect = 4
         */

        if (qtf == null)
        {
            qtf = new QuickTextureField
            {
                Caption = string.Empty,
                FaceType = TextureFactory.TextureObjectType.Text,
                ForegroundColor = Colors.Black,
                Quantity = 1,
            };
        }

        switch (_shape)
        {
            case 0:
                td.Shape = TextureFactory.TokenShape.Square;
                break;

            case 1:
                td.Shape = TextureFactory.TokenShape.Circle;
                break;

            case 2:
                td.Shape = TextureFactory.TokenShape.HexPoint;
                break;

            case 3:
                td.Shape = TextureFactory.TokenShape.HexFlat;
                break;
        }

        td.Objects.Add(
            new TextureFactory.TextureObject
            {
                Scale = 0.8f,
                Width = sW,
                Height = sH,
                CenterX = sW / 2,
                CenterY = sH / 2,
                Multiline = true,
                Text = qtf.Caption,
                ForegroundColor = qtf.ForegroundColor,
                Font = new SystemFont(),
                Type = qtf.FaceType,
                Autosize = true,
                Quantity = qtf.Quantity,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        );

        return td;
    }

    private void FinalizeFrontTexture(ImageTexture t)
    {
        FaceTexture = t;

        if (!_differentBack)
        {
            _backTextureGenerated = true;

            BackTexture = t;
        }

        TextureReady = _frontTextureGenerated && _backTextureGenerated;

        TextureChanged = true;

        MapFrontTexture();

        //var d = t.GetImage();
        //d.SavePng(@"c:\winwam5\token.png");
    }

    private bool _mapFrontTextureRequired;

    private void MapFrontTexture()
    {
        if (!GodotObject.IsInstanceValid(FaceSprite) || !FaceSprite.IsNodeReady())
        {
            _mapFrontTextureRequired = true;
            return;
        }

        _mapFrontTextureRequired = false;

        _frontTextureGenerated = true;

        FaceSprite.Texture = FaceTexture;
        float pixelSize = PixelSize(FaceTexture.GetSize());
        FaceSprite.PixelSize = pixelSize;

        if (!_differentBack)
        {
            BackSprite.PixelSize = pixelSize;
            BackTexture = FaceTexture;
        }
    }

    private bool _mapBackTextureRequired;

    private void MapBackTexture()
    {
        if (!GodotObject.IsInstanceValid(BackSprite) || !BackSprite.IsNodeReady())
        {
            _mapBackTextureRequired = true;
            return;
        }

        _mapBackTextureRequired = false;

        BackSprite.PixelSize = PixelSize(BackTexture.GetSize());
        ;
        BackSprite.Texture = BackTexture;
    }

    private void CreateQuickBackTexture(TextureFactory textureFactory)
    {
        var td = CreateQuickTextureDefinition(_backBgColor, _backField);

        textureFactory.GenerateTexture(td, FinalizeBackTexture);
    }

    private void FinalizeBackTexture(ImageTexture t)
    {
        _backTextureGenerated = true;

        BackTexture = t;

        TextureReady = _frontTextureGenerated && _backTextureGenerated;
        TextureChanged = true;

        MapBackTexture();
    }

    private bool InitializeParameters(
        Dictionary<string, object> parameters,
        TextureFactory textureFactory
    )
    {
        base.Build(parameters, textureFactory);

        var h = Utility.GetParam<float>(parameters, "Height");
        if (h <= 0)
            return false;
        _height = h / 10f;

        var w = Utility.GetParam<float>(parameters, "Width");
        _width = w / 10;

        var t = Utility.GetParam<float>(parameters, "Thickness");
        _thickness = Math.Max(t / 10f, 0.03f);

        _frontImage = Utility.GetParam<string>(parameters, "FrontImage");
        _backImage = Utility.GetParam<string>(parameters, "BackImage");

        _shape = Utility.GetParam<int>(parameters, "Shape");
        _mode = Utility.GetParam<TokenBuildMode>(parameters, "Mode");

        // Quick parameters
        _frontBgColor = Utility.GetParam<Color>(parameters, "FrontBgColor");

        //_frontCaption = Utility.GetParam<string>(parameters, "FrontCaption");
        //_frontCaptionColor = Utility.GetParam<Color>(parameters, "FrontCaptionColor");
        _frontField = Utility.GetParam<QuickTextureField>(parameters, "QuickFront");
        _backField = Utility.GetParam<QuickTextureField>(parameters, "QuickBack");

        _frontFontSize = Utility.GetParam<int>(parameters, "FrontFontSize");
        _differentBack = Utility.GetParam<bool>(parameters, "DifferentBack");

        _backBgColor = Utility.GetParam<Color>(parameters, "BackBgColor");
        _backFontSize = Utility.GetParam<int>(parameters, "BackFontSize");

        //Grid Parameters
        _frontMasterSprite = Utility.GetParam<Texture2D>(parameters, "FrontMasterSprite");
        _backMasterSprite = Utility.GetParam<Texture2D>(parameters, "BackMasterSprite");

        _gridRows = Utility.GetParam<int>(parameters, "GridRows");
        _gridCols = Utility.GetParam<int>(parameters, "GridCols");
        _gridIndex = Utility.GetParam<int>(parameters, "GridIndex");

        if (parameters.TryGetValue("Type", out var tokenType))
        {
            _tokenType = (TokenType)tokenType;
        }
        else
        {
            _tokenType = TokenType.Token; //default
        }

        _frontTemplateName = Utility.GetParam<string>(parameters, "FrontTemplate");
        _backTemplateName = Utility.GetParam<string>(parameters, "BackTemplate");
        _datasetName = Utility.GetParam<string>(parameters, "Dataset");
        if (string.IsNullOrWhiteSpace(DataSetRow))
            DataSetRow = Utility.GetParam<string>(parameters, "CardReference");

        return true;
    }

    public override List<string> ValidateParameters(Dictionary<string, object> parameters)
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

        if (parameters.TryGetValue(nameof(_height), out var height))
        {
            if (height is float h)
            {
                if (h <= 0)
                    ret.Add("Height must be > 0");
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
                if (d <= 0)
                    ret.Add("Diameter must be > 0");
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
    private TokenBuildMode _mode;
    private Color _frontBgColor;
    private QuickTextureField _frontField;
    private QuickTextureField _backField;
    private bool _differentBack;
    private Color _backBgColor;

    private TokenType _tokenType;
    private int _frontFontSize;
    private int _backFontSize;

    //grid parameters
    private Texture2D _frontMasterSprite;
    private Texture2D _backMasterSprite;
    private int _gridRows;
    private int _gridCols;
    private int _gridIndex;

    public enum TokenType
    {
        Card,
        Token,
        Board,
    }
}

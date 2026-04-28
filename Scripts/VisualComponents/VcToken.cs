using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Lizzie.AssetManagement;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using Vector2 = Godot.Vector2;

/// <summary>
/// VcToken represents any object that is flat on two opposite sides, a geometric prism.
/// This includes cards, tiles, and most wood pieces like meeples.
/// VcToken may have a graphic on either of its flat faces, but otherwise has a simple color.
/// </summary>
public partial class VcToken : VisualComponentBase
{
    private MeshInstance3D _mainMesh;
    private StandardMaterial3D _frontMaterial;
    private StandardMaterial3D _backMaterial;

    private const float FaceH = 0.475f;
    private const float FaceR = 0.475f;
    private const int CircleSegments = 32;

    private Texture2D _faceTexture = new ImageTexture();
    private Texture2D _backTexture;

    public Texture2D FaceTexture
    {
        get => _faceTexture;
        set
        {
            _faceTexture = value;
            if (_frontMaterial != null && value != null)
                _frontMaterial.AlbedoTexture = value;
        }
    }

    public Texture2D BackTexture
    {
        get => _backTexture;
        set
        {
            _backTexture = value;
            if (_backMaterial != null && value != null)
                _backMaterial.AlbedoTexture = value;
        }
    }

    public void ForceFace()
    {
        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, 0));
    }

    public void ForceBack()
    {
        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, 180));
    }

    private TokenTextureSubViewport _frontView;
    private TokenTextureSubViewport _backView;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Token;
        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
    }

    public override void _Process(double delta)
    {
        if (_flipInProcess)
        {
            ProcessFlip(delta);
        }

        if (_buildRequired)
        {
            _buildRequired = false;
            GD.PrintErr("VcToken's Build was never called.");
            Build();
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

    public override GeometryInstance3D DragMesh => _mainMesh;
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
    private float _targetZ;
    private bool _flipInProcess;

    private CommandResponse StartFlip()
    {
        _flipInProcess = true;
        _targetZ = RotationDegrees.Z < 90 ? 180 : 0;

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
        float dir = _targetZ == 0 ? -1 : 1;
        float newZ = RotationDegrees.Z + (_flipRate * (float)delta * dir);
        if (dir * (newZ - _targetZ) >= 0)
        {
            newZ = _targetZ;
            _flipInProcess = false;
        }

        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ));
    }

    public enum TokenBuildMode
    {
        Quick,
        Custom,
        Grid,
        Template,
        Nandeck,
        QuickDeck, //need to parse the QuickBuild string to pull out the caption
    }

    private bool _buildRequired = true;

    public override bool Setup(
        Dictionary<string, object> parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        base.Setup(parameters, dataSetRow, textureFactory);

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
        _frontGridImageKey = Utility.GetParam<string>(parameters, "FrontGridImageKey");
        if (string.IsNullOrEmpty(_frontGridImageKey))
        {
            _frontMasterAsset = null;
            _frontMasterSprite = new ImageTexture();
        }
        else
        {
            ProjectService.Instance.CurrentProject.Images.TryGetValue(
                _frontGridImageKey,
                out _frontMasterAsset
            );
        }

        _backGridImageKey = Utility.GetParam<string>(parameters, "BackGridImageKey");
        if (string.IsNullOrEmpty(_backGridImageKey))
        {
            _backMasterAsset = null;
            _backMasterSprite = new ImageTexture();
        }
        else
        {
            ProjectService.Instance.CurrentProject.Images.TryGetValue(
                _backGridImageKey,
                out _backMasterAsset
            );
        }

        _gridRows = Utility.GetParam<int>(parameters, "GridRows");
        _gridCols = Utility.GetParam<int>(parameters, "GridCols");
        _gridCount = Utility.GetParam<int>(parameters, "GridCount");
        _gridSingleBack = Utility.GetParam<bool>(parameters, "GridSingleBack");
        //_gridIndex = Utility.GetParam<int>(parameters, "GridIndex");

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

        _quickCardList = Utility.GetParam<List<QuickCardData>>(parameters, "QuickCardData");

        if (_quickCardList == null)
            _quickCardList = new();

        return true;
    }

    public override void Build()
    {
        if (!IsNodeReady())
        {
            GD.PrintErr("VcToken was built before the node was ready.");
            return;
        }

        this._buildRequired = false;

        BuildToken();

        switch (_mode)
        {
            case TokenBuildMode.Quick:
                BuildQuick(TextureFactory);
                break;

            case TokenBuildMode.Custom:
                BuildCustom();
                break;

            case TokenBuildMode.Grid:
                BuildGrid();
                break;

            case TokenBuildMode.Template:
                BuildTemplate(TextureFactory);
                break;

            case TokenBuildMode.Nandeck:
                BuildNanDeck();
                break;

            case TokenBuildMode.QuickDeck:
                BuildQuickDeck(TextureFactory);
                break;
        }
    }

    public override bool Setup(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        return Setup(parameters, DataSetRow, textureFactory);
    }

    private void BuildToken()
    {
        _mainMesh = GetNode<MeshInstance3D>("SideMesh");

        YHeight = _thickness;
        Scale = new Vector3(_width, _thickness, _height);

        _frontMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _backMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var sideMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.506f, 0.506f, 0.506f),
        };

        var shape = (TokenTextureSubViewport.TokenShape)_shape;
        var ring = GetFaceRing(shape);

        var mesh = new ArrayMesh();
        CommitFaceSurface(mesh, _frontMaterial, ring, +FaceH, mirrorU: false);
        CommitFaceSurface(mesh, _backMaterial, ring, -FaceH, mirrorU: true);
        CommitSideSurface(mesh, sideMaterial, ring);
        _mainMesh.Mesh = mesh;

        var highlightShader = GD.Load<Shader>("res://Shaders/outline2.gdshader");
        var highlightMat = new ShaderMaterial { Shader = highlightShader };
        highlightMat.SetShaderParameter("outline_color", Colors.White);
        highlightMat.SetShaderParameter("border_width", 0.04f);
        HighlightMesh.Mesh = mesh;
        HighlightMesh.MaterialOverride = highlightMat;

        ShapeProfiles.Clear();
        switch (shape)
        {
            case TokenTextureSubViewport.TokenShape.Square:
                ShapeProfiles.Add(
                    new OffsetShape2D(new RectangleShape2D { Size = new Vector2(_width, _height) })
                );
                break;
            case TokenTextureSubViewport.TokenShape.Circle:
                ShapeProfiles.Add(new OffsetShape2D(new CircleShape2D { Radius = _width / 2f }));
                break;
            case TokenTextureSubViewport.TokenShape.HexPoint:
            case TokenTextureSubViewport.TokenShape.HexFlat:
                var poly = new ConvexPolygonShape2D();
                poly.Points = ring.Select(v => new Vector2(v.X, v.Z)).ToArray();
                ShapeProfiles.Add(new OffsetShape2D(poly));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private Vector3[] GetFaceRing(TokenTextureSubViewport.TokenShape shape) =>
        shape switch
        {
            TokenTextureSubViewport.TokenShape.Square => new[]
            {
                new Vector3(-FaceR, 0, -FaceR),
                new Vector3(+FaceR, 0, -FaceR),
                new Vector3(+FaceR, 0, +FaceR),
                new Vector3(-FaceR, 0, +FaceR),
            },
            TokenTextureSubViewport.TokenShape.Circle => Enumerable
                .Range(0, CircleSegments)
                .Select(i =>
                {
                    float a = i * Mathf.Tau / CircleSegments;
                    return new Vector3(FaceR * Mathf.Cos(a), 0, FaceR * Mathf.Sin(a));
                })
                .ToArray(),
            TokenTextureSubViewport.TokenShape.HexPoint => HexRing(Mathf.Pi / 2f),
            TokenTextureSubViewport.TokenShape.HexFlat => HexRing(0f),
            _ => new[]
            {
                new Vector3(-FaceR, 0, -FaceR),
                new Vector3(+FaceR, 0, -FaceR),
                new Vector3(+FaceR, 0, +FaceR),
                new Vector3(-FaceR, 0, +FaceR),
            },
        };

    private static Vector3[] HexRing(float startAngle) =>
        Enumerable
            .Range(0, 6)
            .Select(i =>
            {
                float a = startAngle + i * Mathf.Tau / 6f;
                return new Vector3(FaceR * Mathf.Cos(a), 0, FaceR * Mathf.Sin(a));
            })
            .ToArray();

    private static void CommitFaceSurface(
        ArrayMesh mesh,
        StandardMaterial3D mat,
        Vector3[] ring,
        float y,
        bool mirrorU
    )
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(mat);
        var normal = mirrorU ? Vector3.Down : Vector3.Up;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            // front: (center, a, b) → +Y normal; back: (center, b, a) → −Y normal
            AddFaceVert(st, Vector3.Zero, y, normal, mirrorU);
            if (!mirrorU)
            {
                AddFaceVert(st, a, y, normal, false);
                AddFaceVert(st, b, y, normal, false);
            }
            else
            {
                AddFaceVert(st, b, y, normal, true);
                AddFaceVert(st, a, y, normal, true);
            }
        }
        st.Commit(mesh);
    }

    private static void AddFaceVert(
        SurfaceTool st,
        Vector3 xz,
        float y,
        Vector3 normal,
        bool mirrorU
    )
    {
        st.SetNormal(normal);
        float u = 0.5f + xz.X / (2f * FaceR);
        if (mirrorU)
            u = 1f - u;
        st.SetUV(new Vector2(u, 0.5f + xz.Z / (2f * FaceR)));
        st.AddVertex(new Vector3(xz.X, y, xz.Z));
    }

    private static void CommitSideSurface(ArrayMesh mesh, StandardMaterial3D mat, Vector3[] ring)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(mat);
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            var outward = new Vector3((a.X + b.X) * 0.5f, 0, (a.Z + b.Z) * 0.5f).Normalized();
            var aT = new Vector3(a.X, +FaceH, a.Z);
            var bT = new Vector3(b.X, +FaceH, b.Z);
            var aB = new Vector3(a.X, -FaceH, a.Z);
            var bB = new Vector3(b.X, -FaceH, b.Z);
            st.SetNormal(outward);
            st.AddVertex(aT);
            st.SetNormal(outward);
            st.AddVertex(aB);
            st.SetNormal(outward);
            st.AddVertex(bT);
            st.SetNormal(outward);
            st.AddVertex(bT);
            st.SetNormal(outward);
            st.AddVertex(aB);
            st.SetNormal(outward);
            st.AddVertex(bB);
        }
        st.Commit(mesh);
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
        CreateCustomFrontTexture();

        if (_differentBack)
        {
            _backView = GetNode<TokenTextureSubViewport>("BackViewport");
            CreateCustomBackTexture();
        }
    }

    private void BuildGrid()
    {
        if (_frontMasterAsset == null)
        {
            _frontMasterSprite = new ImageTexture();
            ApplyGridFaceTexture();
        }
        else
        {
            ProjectService.Instance.FetchImageAsync(_frontMasterAsset, BuildGridFace);
        }

        if (_differentBack)
        {
            if (_backMasterAsset == null)
            {
                _backMasterSprite = new ImageTexture();
                ApplyGridBackTexture();
            }
            else
            {
                ProjectService.Instance.FetchImageAsync(_backMasterAsset, BuildGridBack);
            }
        }
    }

    private void BuildGridFace(Asset asset)
    {
        if (!asset.AssetDownloaded)
            return;

        var texture = new ImageTexture();
        texture.SetImage(asset.Image);
        _frontMasterSprite = texture;

        ApplyGridFaceTexture();
    }

    private void ApplyGridFaceTexture()
    {
        if (!_differentBack)
            _backTextureGenerated = true;
        FaceTexture = _frontMasterSprite;
        MapFrontTexture();
    }

    private void BuildGridBack(Asset asset)
    {
        if (!asset.AssetDownloaded)
            return;

        var texture = new ImageTexture();
        texture.SetImage(asset.Image);
        _backMasterSprite = texture;

        ApplyGridBackTexture();
    }

    private void ApplyGridBackTexture()
    {
        BackTexture = _backMasterSprite;
        MapBackTexture();
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

        if (ft is null)
            return;

        TextureContext context = new TextureContext
        {
            DataSet = ds,
            Dpi = 100,
            ParentSize = new Vector2(ft.Width * 10, ft.Height * 10),
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

    private List<QuickCardData> _quickCardList = new();

    private void BuildQuickDeck(TextureFactory textureFactory)
    {
        int.TryParse(DataSetRow, out var r);

        if (r == 0)
            return;

        var qtf = TemplateEngine.GenerateQuickCardByRow(_quickCardList, _quickCardList.Count, r);
        var td = CreateQuickTextureDefinition(
            qtf.BackgroundColor,
            new QuickTextureField
            {
                Caption = qtf.Caption,
                FaceType = TextureFactory.TextureObjectType.Text,
                ForegroundColor = Colors.Black,
                Quantity = 1,
            }
        );

        textureFactory.GenerateTexture(td, FinalizeFrontTexture);

        if (_differentBack)
        {
            _backBgColor = qtf.CardBackColor;
            _backField = new QuickTextureField
            {
                Caption = qtf.CardBackValue,
                FaceType = TextureFactory.TextureObjectType.Text,
                ForegroundColor = Colors.Black,
                Quantity = 1,
            };
            CreateQuickBackTexture(textureFactory);
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

        FaceTexture = _frontView.GetTexture();

        if (!_differentBack)
        {
            BackTexture = FaceTexture;
            _backTextureGenerated = true;
        }
    }

    private void CreateCustomBackTexture()
    {
        if (!File.Exists(_backImage))
            return;

        _backView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
        _backView.SetShape((TokenTextureSubViewport.TokenShape)_shape);
        _backView.SetTexture(LoadTexture(_backImage));
        BackTexture = _backView.GetTexture();
        _backTextureGenerated = true;
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
        if (_frontMaterial == null)
        {
            _mapFrontTextureRequired = true;
            return;
        }

        _mapFrontTextureRequired = false;
        _frontTextureGenerated = true;
        _frontMaterial.AlbedoTexture = FaceTexture;

        if (_mode == TokenBuildMode.Grid)
        {
            int.TryParse(DataSetRow, out var r);
            int cols = Math.Max(_gridCols, 1);
            int rows = Math.Max(_gridRows, 1);
            int col = r % cols;
            int row = r / cols;
            _frontMaterial.Uv1Scale = new Vector3(1f / cols, 1f / rows, 1f);
            _frontMaterial.Uv1Offset = new Vector3((float)col / cols, (float)row / rows, 0f);
        }

        if (!_differentBack)
            BackTexture = FaceTexture;
    }

    private bool _mapBackTextureRequired;

    private void MapBackTexture()
    {
        if (_backMaterial == null)
        {
            _mapBackTextureRequired = true;
            return;
        }

        _mapBackTextureRequired = false;
        _backTextureGenerated = true;
        _backMaterial.AlbedoTexture = BackTexture;

        if (_mode == TokenBuildMode.Grid && !_gridSingleBack)
        {
            int.TryParse(DataSetRow, out var r);
            int cols = Math.Max(_gridCols, 1);
            int rows = Math.Max(_gridRows, 1);
            int col = r % cols;
            int row = r / cols;
            _backMaterial.Uv1Scale = new Vector3(1f / cols, 1f / rows, 1f);
            _backMaterial.Uv1Offset = new Vector3((float)col / cols, (float)row / rows, 0f);
        }
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
    private Asset _frontMasterAsset;
    private Asset _backMasterAsset;
    private string _frontGridImageKey;
    private string _backGridImageKey;

    private int _gridRows;
    private int _gridCols;

    private int _gridCount;
    private bool _gridSingleBack;

    //private int _gridIndex;

    public enum TokenType
    {
        Card,
        Token,
        Board,
    }
}

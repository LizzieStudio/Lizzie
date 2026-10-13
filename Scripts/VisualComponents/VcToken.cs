using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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

    private const float FaceH = 0.5f;
    private const float FaceR = 0.5f;
    private const int CircleSegments = 32;

    // The mesh's surfaces.
    private const int FrontSurface = 0;
    private const int BackSurface = 1;
    private const int SideSurface = 2;

    // Tokens share their meshes and materials, so the renderer can draw alike tokens in one call.
    private static readonly Dictionary<TokenShape, ArrayMesh> Meshes = new();

    private static readonly StandardMaterial3D SideMaterial = new()
    {
        AlbedoColor = new Color(0.506f, 0.506f, 0.506f),
    };

    private static readonly Shader FaceShader = GD.Load<Shader>(
        "res://Shaders/token_face.gdshader"
    );

    // A face with no texture yet, which shows white.
    private static readonly ShaderMaterial BlankFace = new() { Shader = FaceShader };

    private static readonly ConditionalWeakTable<Texture2D, ShaderMaterial> FaceMaterials = new();

    /// <summary>
    /// The material shared by every face showing <paramref name="texture"/>.
    /// </summary>
    private static ShaderMaterial FaceMaterial(Texture2D texture)
    {
        if (texture == null)
            return BlankFace;
        if (!FaceMaterials.TryGetValue(texture, out var material))
        {
            material = new ShaderMaterial { Shader = FaceShader };
            material.SetShaderParameter("albedo_texture", texture);
            FaceMaterials.Add(texture, material);
        }
        return material;
    }

    private Texture2D _faceTexture = new ImageTexture();
    private Texture2D _backTexture;

    public Texture2D FaceTexture
    {
        get => _faceTexture;
        private set
        {
            _faceTexture = value;

            if (_mainMesh != null && value != null)
                _mainMesh.SetSurfaceOverrideMaterial(FrontSurface, FaceMaterial(value));
            TexturesChanged?.Invoke();
        }
    }

    public Texture2D BackTexture
    {
        get => _backTexture;
        set
        {
            _backTexture = value;
            if (_mainMesh != null && value != null)
                _mainMesh.SetSurfaceOverrideMaterial(BackSurface, FaceMaterial(value));
            TexturesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Called when the face or back texture is changes.
    /// </summary>
    public event Action TexturesChanged;

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Token;
    }

    public override void _Process(double delta)
    {
        if (_flipInProcess)
        {
            ProcessFlip(delta);
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

        // Idle optimization.
        if (!_flipInProcess && !_mapFrontTextureRequired && !_mapBackTextureRequired)
            SetProcess(false);

        base._Process(delta);
    }

    public override GeometryInstance3D DragMesh => _mainMesh;
    public override float MaxAxisSize => Math.Max(_height, _width);

    public Vector2 Size => new(_width, _height);

    private float _flipRate = 720; //degrees per second
    private float _targetZ;
    private bool _flipInProcess;

    public override bool PlayTransition(
        ComponentState s,
        CommandName? writtenBy,
        long MsecSinceStart
    )
    {
        if (writtenBy != ComponentCommands.Flip.Name || MsecSinceStart >= 1000f * 180f / _flipRate)
            return false;

        _flipInProcess = true;
        SetProcess(true);
        _targetZ = Mathf.RadToDeg(s.Rotation.Z);
        return true;
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

        RotationDegrees = new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ);
    }

    public enum TokenBuildMode
    {
        QuickCard,
        Custom,
        Grid,
        Template,
        Nandeck,
        QuickDeck, //need to parse the QuickBuild string to pull out the caption
    }

    protected override void Setup(ComponentParameters parameters, IRecordReader R)
    {
        base.Setup(parameters, R);
        Apply((PrintedParameters)parameters, R);
    }

    /// <summary>
    /// Counts builds so a stale build can be thrown out.
    /// </summary>
    private int _build;

    /// <summary>Sizes and textures the token from its parameters and the records they reference.</summary>
    private void Apply(PrintedParameters p, IRecordReader R)
    {
        if (p.Height <= 0)
            return;
        _height = p.Height / 10f;

        _width = p.Width / 10;

        _thickness = Math.Max(p.Thickness / 10f, 0.03f);

        _frontImage = p.FrontImage;
        _backImage = p.BackImage;

        Shape = (TokenShape)p.Shape;
        _mode = p.Mode;

        _differentBack = p.DifferentBack;

        //Grid Parameters
        _frontGridImageKey = p.FrontGridImageKey;
        _frontMasterAsset = R.Get<ImageAsset>(_frontGridImageKey);

        _backGridImageKey = p.BackGridImageKey;
        _backMasterAsset = R.Get<ImageAsset>(_backGridImageKey);

        _tokenType = p.Type;

        _frontTemplateRef = p.FrontTemplate;
        _backTemplateRef = p.BackTemplate;
        _datasetRef = p.Dataset;

        _faceHframes = 1;
        _faceVframes = 1;
        _faceFrame = 0;
        _backHframes = 1;
        _backVframes = 1;
        _backFrame = 0;

        if (_mode == TokenBuildMode.Grid)
        {
            var (front, back) = p.GridFrames(R);
            int card = Math.Max(DataSetRowIndex, 0);
            _frontGridFace = GridFace(_frontMasterAsset, front, card);
            _backGridFace = GridFace(_backMasterAsset, back, card);
            if (_frontGridFace is (var frontLayout, var frontFrame))
                (_faceHframes, _faceVframes, _faceFrame) = (
                    frontLayout.Columns,
                    frontLayout.Rows,
                    frontFrame
                );
            if (_backGridFace is (var backLayout, var backFrame))
                (_backHframes, _backVframes, _backFrame) = (
                    backLayout.Columns,
                    backLayout.Rows,
                    backFrame
                );
        }

        BuildToken();

        _build++;
        switch (_mode)
        {
            case TokenBuildMode.QuickCard:
            case TokenBuildMode.QuickDeck:
                BuildQuick(TextureFactory, GetPrototype(R));
                break;

            case TokenBuildMode.Custom:
                BuildCustom();
                break;

            case TokenBuildMode.Grid:
                BuildGrid();
                break;

            case TokenBuildMode.Template:
                BuildTemplate(TextureFactory, R);
                break;

            case TokenBuildMode.Nandeck:
                BuildNanDeck();
                break;
        }
    }

    private void BuildToken()
    {
        _mainMesh = GetNode<MeshInstance3D>("SideMesh");

        YHeight = _thickness;
        Scale = new Vector3(_width, _thickness, _height);

        if (!Meshes.TryGetValue(Shape, out var mesh))
        {
            var ring = GetFaceRing(Shape);
            mesh = new ArrayMesh();
            CommitFaceSurface(mesh, ring, +FaceH, mirrorU: false);
            CommitFaceSurface(mesh, ring, -FaceH, mirrorU: true);
            CommitSideSurface(mesh, ring);
            Meshes[Shape] = mesh;
        }
        _mainMesh.Mesh = mesh;
        _mainMesh.SetSurfaceOverrideMaterial(FrontSurface, BlankFace);
        _mainMesh.SetSurfaceOverrideMaterial(BackSurface, BlankFace);
        _mainMesh.SetSurfaceOverrideMaterial(SideSurface, SideMaterial);
        SetFrame("front_rect", 1, 1, 0);
        SetFrame("back_rect", 1, 1, 0);

        ShapeProfiles.Clear();
        ShapeProfiles.Add(ShapeProfile(Shape, _width, _height));
    }

    /// <summary>
    /// The table footprint of a <paramref name="width"/> by <paramref name="height"/> token, used
    /// for stacking.
    /// </summary>
    public static OffsetShape2D ShapeProfile(TokenShape shape, float width, float height) =>
        shape switch
        {
            TokenShape.Rectangle => new(new RectangleShape2D { Size = new Vector2(width, height) }),
            TokenShape.Circle => new(new CircleShape2D { Radius = width / 2f }),
            TokenShape.HexPoint or TokenShape.HexFlat => new(
                new ConvexPolygonShape2D
                {
                    Points = GetFaceRing(shape)
                        .Select(v => new Vector2(
                            v.X / (2 * FaceR) * width,
                            v.Z / (2 * FaceR) * height
                        ))
                        .ToArray(),
                }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

    private static Vector3[] GetFaceRing(TokenShape shape) =>
        shape switch
        {
            TokenShape.Rectangle => new[]
            {
                new Vector3(-FaceR, 0, -FaceR),
                new Vector3(+FaceR, 0, -FaceR),
                new Vector3(+FaceR, 0, +FaceR),
                new Vector3(-FaceR, 0, +FaceR),
            },
            TokenShape.Circle => Enumerable
                .Range(0, CircleSegments)
                .Select(i =>
                {
                    float a = i * Mathf.Tau / CircleSegments;
                    return new Vector3(FaceR * Mathf.Cos(a), 0, FaceR * Mathf.Sin(a));
                })
                .ToArray(),
            TokenShape.HexPoint => HexRing(Mathf.Pi / 2f),
            TokenShape.HexFlat => HexRing(0f),
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

    private static void CommitFaceSurface(ArrayMesh mesh, Vector3[] ring, float y, bool mirrorU)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
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

    private static void CommitSideSurface(ArrayMesh mesh, Vector3[] ring)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
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

    private void BuildQuick(TextureFactory textureFactory, Prototype prototype)
    {
        var p = (PrintedParameters)prototype.Parameters;
        ComputeCellSize(_height, _width, out var cellW, out var cellH);
        Sheet Side(string side, List<DataRow> rows) =>
            new(
                $"quick:{prototype.SheetKey()}:{side}",
                QuickTemplate.Template,
                QuickTemplate.DataSet,
                rows,
                cellW,
                cellH
            );

        if (_mode == TokenBuildMode.QuickCard)
        {
            BuildSheets(
                textureFactory,
                Side("f", [QuickTemplate.Row(p.FrontBgColor, p.QuickFront)]),
                _differentBack ? Side("b", [QuickTemplate.Row(p.BackBgColor, p.QuickBack)]) : null,
                0
            );
        }
        else if (_mode == TokenBuildMode.QuickDeck)
        {
            var cards = p.QuickDeckCards();
            if (DataSetRowIndex < 0 || cards.Count == 0)
                return;
            DataRow Text(Color background, string caption) =>
                QuickTemplate.Row(
                    background,
                    new QuickTextureField { Caption = caption, ForegroundColor = Colors.Black }
                );
            BuildSheets(
                textureFactory,
                Side("f", cards.Select(c => Text(c.BackgroundColor, c.Caption)).ToList()),
                Side("b", cards.Select(c => Text(c.CardBackColor, c.CardBackValue)).ToList()),
                DataSetRowIndex
            );
        }
        else
        {
            throw new NotImplementedException("BuildQuick can only handle QuickCard and QuickDeck");
        }
    }

    private void BuildCustom()
    {
        CreateCustomFrontTexture();
        if (_differentBack)
            CreateCustomBackTexture();
    }

    /// <summary>
    /// The image frame a grid card shows on one side, or null.
    /// </summary>
    private static (GridLayout Layout, int Frame)? GridFace(
        ImageAsset asset,
        int[] values,
        int card
    ) =>
        asset != null && card < values.Length && asset.Layout.Frame(values[card]) is int frame
            ? (asset.Layout, frame)
            : null;

    private void BuildGrid()
    {
        BuildGridFace(_frontGridFace == null ? null : _frontMasterAsset);
        if (_differentBack)
            BuildGridBack(_backGridFace == null ? null : _backMasterAsset);
    }

    private async void BuildGridFace(ImageAsset asset)
    {
        int build = _build;
        var texture = await TextureCache.Instance.GetOrCreateAssetTexture(asset);
        // The card may have been freed, or rebuilt, while the image loaded.
        if (IsInstanceValid(this) && build == _build)
            ApplyGridFaceTexture(texture ?? MissingFrameTexture);
    }

    private void ApplyGridFaceTexture(Texture2D texture)
    {
        if (!_differentBack)
            _backTextureGenerated = true;
        FaceTexture = texture;
        MapFrontTexture();
    }

    private async void BuildGridBack(ImageAsset asset)
    {
        int build = _build;
        var texture = await TextureCache.Instance.GetOrCreateAssetTexture(asset);
        if (IsInstanceValid(this) && build == _build)
            ApplyGridBackTexture(texture ?? MissingFrameTexture);
    }

    private void ApplyGridBackTexture(Texture2D texture)
    {
        BackTexture = texture;
        MapBackTexture();
    }

    private void UpdateDerivedSheet()
    {
        _frontTextureGenerated = true;
        _backTextureGenerated = true;
        MapFrontTexture();
        MapBackTexture();
    }

    private SnowTag _frontTemplateRef;
    private SnowTag _backTemplateRef;
    private SnowTag _datasetRef;

    private void BuildTemplate(TextureFactory textureFactory, IRecordReader R)
    {
        _differentBack = true;

        if (_height <= 0 || _width <= 0)
            return;

        if (_frontTemplateRef == SnowTag.Empty)
            return;

        var ft = R.Get<Template>(_frontTemplateRef);
        var ds = R.Get<DataSet>(_datasetRef);
        var bt = R.Get<Template>(_backTemplateRef);

        if (ft is null || ds is null)
            return;

        var rows = R.GetRows(_datasetRef);
        if (rows.Count == 0)
            return;

        int faceFrame = rows.FindIndex(r => r.Id == DataSetRowId);
        if (faceFrame < 0)
            return;

        string rowsKey = rows.SheetKey();
        Sheet Side(Template template)
        {
            if (template == null)
                return null;
            var (cellW, cellH) = template.Pixels(10 * BASE_DPI);
            if (cellW <= 0 || cellH <= 0)
                return null;
            return new(
                $"tpl:{template.SheetKey()}:{ds.SheetKey()}:{rowsKey}",
                template,
                ds,
                rows,
                cellW,
                cellH
            );
        }

        var front = Side(ft);
        if (front == null)
            return;

        BuildSheets(textureFactory, front, Side(bt), faceFrame);
    }

    private const float BASE_DPI = 10f;

    /// <summary>
    /// One side's sheet: <see cref="Template"/> drawn once per row, each in a
    /// <see cref="CellWidth"/> by <see cref="CellHeight"/> cell, and cached under <see cref="Key"/>.
    /// </summary>
    private sealed record Sheet(
        string Key,
        Template Template,
        DataSet DataSet,
        List<DataRow> Rows,
        int CellWidth,
        int CellHeight
    );

    /// <summary>
    /// Shows <paramref name="frame"/> of the front and back sheets, building whichever isn't
    /// cached yet. With no <paramref name="back"/>, the back shows the front.
    /// </summary>
    private async void BuildSheets(
        TextureFactory textureFactory,
        Sheet front,
        Sheet back,
        int frame
    )
    {
        int build = _build;
        int n = front.Rows.Count;
        int hframes = (int)Math.Ceiling(Math.Sqrt(n));
        int vframes = (int)Math.Ceiling((double)n / hframes);

        Task<Texture2D> Request(Sheet sheet) =>
            TextureCache.Instance.GetOrBuildDerived(
                sheet.Key,
                () =>
                {
                    var defs = new List<TextureFactory.TextureDefinition>(sheet.Rows.Count);
                    var ctx = new TextureContext
                    {
                        DataSet = sheet.DataSet,
                        Dpi = 10 * BASE_DPI,
                        ParentSize = new Vector2(sheet.CellWidth, sheet.CellHeight),
                    };
                    foreach (var row in sheet.Rows)
                    {
                        ctx.CurrentRow = row;
                        defs.Add(TemplateEngine.GenerateTextureDefinition(sheet.Template, ctx));
                    }
                    return new SpriteSheetBuilder(
                        textureFactory,
                        defs,
                        sheet.CellWidth,
                        sheet.CellHeight,
                        hframes,
                        vframes
                    ).Start();
                }
            );

        var frontSheet = Request(front);
        var backSheet = back == null ? frontSheet : Request(back);
        var frontTexture = await frontSheet;
        var backTexture = await backSheet;
        // The card may have been freed, or rebuilt, while the sheets rendered.
        if (IsInstanceValid(this) && build == _build)
        {
            _faceHframes = hframes;
            _faceVframes = vframes;
            _faceFrame = frame;
            _backHframes = hframes;
            _backVframes = vframes;
            _backFrame = frame;
            FaceTexture = frontTexture;
            BackTexture = backTexture;
            UpdateDerivedSheet();
        }
    }

    private static void ComputeCellSize(float height, float width, out int cellW, out int cellH)
    {
        cellW = 256;
        cellH = 256;
        if (height <= 0 || width <= 0)
            return;
        if (height > width)
            cellW = (int)(width * 256 / height);
        else
            cellH = (int)(height * 256 / width);
    }

    private bool _frontTextureGenerated;
    private bool _backTextureGenerated;

    private void BuildNanDeck() { }

    private void BuildImport() { }

    private void CreateCustomFrontTexture()
    {
        if (!File.Exists(_frontImage))
            return;

        FaceTexture = LoadImage(_frontImage);
        _frontTextureGenerated = true;

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

        BackTexture = LoadImage(_backImage);
        _backTextureGenerated = true;
    }

    private static ImageTexture LoadImage(string path) =>
        Image.LoadFromFile(path) is { } image
            ? ImageTexture.CreateFromImage(image)
            : new ImageTexture();

    private bool _mapFrontTextureRequired;

    private void MapFrontTexture()
    {
        if (_mainMesh == null)
        {
            _mapFrontTextureRequired = true;
            SetProcess(true);
            return;
        }

        _mapFrontTextureRequired = false;
        _frontTextureGenerated = true;
        _mainMesh.SetSurfaceOverrideMaterial(FrontSurface, FaceMaterial(FaceTexture));

        SetFrame("front_rect", _faceHframes, _faceVframes, _faceFrame);

        if (!_differentBack)
            BackTexture = FaceTexture;
    }

    private bool _mapBackTextureRequired;

    private void MapBackTexture()
    {
        if (_mainMesh == null)
        {
            _mapBackTextureRequired = true;
            SetProcess(true);
            return;
        }

        _mapBackTextureRequired = false;
        _backTextureGenerated = true;
        _mainMesh.SetSurfaceOverrideMaterial(BackSurface, FaceMaterial(BackTexture));
        SetFrame("back_rect", _backHframes, _backVframes, _backFrame);
    }

    private void SetFrame(string face, int hframes, int vframes, int frame)
    {
        int cols = Math.Max(hframes, 1);
        int rows = Math.Max(vframes, 1);
        int col = frame % cols;
        int row = frame / cols;
        _mainMesh.SetInstanceShaderParameter(
            face,
            new Vector4((float)col / cols, (float)row / rows, 1f / cols, 1f / rows)
        );
    }

    private float _height;
    private float _width;
    private float _thickness;
    private string _frontImage;
    private string _backImage;

    public TokenShape Shape { get; private set; }
    private TokenBuildMode _mode;
    private bool _differentBack;

    private TokenType _tokenType;

    //grid parameters
    private ImageAsset _frontMasterAsset;
    private ImageAsset _backMasterAsset;
    private SnowTag _frontGridImageKey;
    private SnowTag _backGridImageKey;

    private (GridLayout Layout, int Frame)? _frontGridFace;
    private (GridLayout Layout, int Frame)? _backGridFace;

    /// <summary>
    /// Shown in place of a grid frame that doesn't exist.
    /// </summary>
    private static readonly Texture2D MissingFrameTexture = GD.Load<Texture2D>(
        "res://Textures/missing_frame.png"
    );

    private int _faceHframes = 1;
    private int _faceVframes = 1;
    private int _faceFrame = 0;
    private int _backHframes = 1;
    private int _backVframes = 1;
    private int _backFrame = 0;

    public int FaceHframes => _faceHframes;
    public int FaceVframes => _faceVframes;
    public int FaceFrame => _faceFrame;
    public int BackHframes => _backHframes;
    public int BackVframes => _backVframes;
    public int BackFrame => _backFrame;

    public enum TokenType
    {
        Card,
        Token,
        Board,
    }
}

public abstract record PrintedParameters : ComponentParameters
{
    public float Height { get; init; }
    public float Width { get; init; }
    public float Thickness { get; init; }
    public int Shape { get; init; }
    public VcToken.TokenBuildMode Mode { get; init; }
    public bool DifferentBack { get; init; }

    public string FrontImage { get; init; } = "";
    public string BackImage { get; init; } = "";

    public Color FrontBgColor { get; init; } = Colors.Black;
    public Color BackBgColor { get; init; } = Colors.Black;

    public QuickTextureField QuickFront { get; init; } = new();
    public QuickTextureField QuickBack { get; init; } = new();

    public int FrontFontSize { get; init; }
    public int BackFontSize { get; init; }

    public VcToken.TokenType Type { get; init; }

    public ImmutableArray<QuickCardData> QuickCardData { get; init; } =
        ImmutableArray<QuickCardData>.Empty;

    /// <summary>
    /// Converts Quick Deck parameters into a bunch of Quick Card data.
    /// </summary>
    public List<QuickCardData> QuickDeckCards() =>
        QuickCardData
            .SelectMany(q =>
                Utility
                    .ParseValueRanges(q.Caption)
                    .Select(v => new QuickCardData
                    {
                        Caption = v,
                        BackgroundColor = q.BackgroundColor,
                        CardBackValue = q.CardBackValue,
                        CardBackColor = q.CardBackColor,
                    })
            )
            .ToList();

    public SnowTag FrontGridImageKey { get; init; }
    public SnowTag BackGridImageKey { get; init; }

    /// <summary>
    /// The front image's Range Expression provided by the user.
    /// </summary>
    public string FrontGridFrames { get; init; } = "";

    /// <summary>
    /// The back image's Range Expression provided by the user.
    /// </summary>
    public string BackGridFrames { get; init; } = "";

    /// <summary>
    /// Computes how many cards a grid deck should have.
    /// </summary>
    public int GridFrameCount(IRecordReader R)
    {
        var (front, back) = GridFrames(R);
        return Math.Max(front.Count(), back.Count());
    }

    /// <summary>
    /// The 1-based frame indexes each grid card shows on its front and back.
    /// </summary>
    public (int[] Front, int[] Back) GridFrames(IRecordReader R)
    {
        var front = R.Get<ImageAsset>(FrontGridImageKey)?.Layout ?? GridLayout.Single;
        var back = R.Get<ImageAsset>(BackGridImageKey)?.Layout ?? GridLayout.Single;
        front.TryParse(FrontGridFrames, out var frontValues);
        back.TryParse(BackGridFrames, out var backValues);
        int count = Math.Max(frontValues.Length, backValues.Length);
        front.TryParse(FrontGridFrames, out frontValues, count);
        back.TryParse(BackGridFrames, out backValues, count);
        return (frontValues, backValues);
    }

    public SnowTag FrontTemplate { get; init; }
    public SnowTag BackTemplate { get; init; }
    public SnowTag Dataset { get; init; }
}

[JsonName("TokenParameters")]
public sealed record TokenParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Token;
}

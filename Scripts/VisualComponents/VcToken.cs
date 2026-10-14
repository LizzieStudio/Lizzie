using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        Shape = (TokenShape)p.Shape;
        _tokenType = p.Type;

        BuildToken();

        _build++;
        var prototype = GetPrototype(R);
        int count = p.CardCount(R);
        var front = View(p.Front, "f", Card.FrontRow, prototype, count, R);
        var back =
            p.Back == null ? front : View(p.Back, "b", Card.BackRow, prototype, count, R) ?? front;
        ShowSides(front, back);
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
        SetFrame("front_rect", GridLayout.Single, 0);
        SetFrame("back_rect", GridLayout.Single, 0);

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

    /// <summary>
    /// A side's texture, as it loads, and which frame of it the card shows.
    /// </summary>
    private sealed record SideView(Task<Texture2D> Texture, GridLayout Layout, int Frame);

    /// <summary>
    /// What <paramref name="side"/> shows on this card, or null when it shows nothing.
    /// </summary>
    /// <param name="key">The key to use for the texture cache.</param>
    /// <param name="row">The dataset row this side shows, for a template.</param>
    /// <param name="count">How many cards the prototype has.</param>
    private SideView View(
        SideParameters side,
        string key,
        SnowTag row,
        Prototype prototype,
        int count,
        IRecordReader R
    )
    {
        int n = Math.Max(Card.Number, 0);
        switch (side)
        {
            case QuickSide quick:
            {
                var cards = quick.Cards();
                if (cards.Count == 0)
                    return null;
                // Cards that repeat are only drawn once.
                // This isn't exhaustive, but it catches things like A*2
                var distinct = cards.Distinct().ToList();
                return SheetView(
                    new Sheet(
                        $"quick:{prototype.SheetKey()}:{key}",
                        QuickTemplate.Template with
                        {
                            Width = _width / CmPerInch,
                            Height = _height / CmPerInch,
                        },
                        QuickTemplate.DataSet,
                        distinct.Select(c => QuickTemplate.Row(c.Suit, c.Value)).ToList()
                    ),
                    distinct.IndexOf(cards[n % cards.Count])
                );
            }

            case ImageSide image:
            {
                var asset = R.Get<ImageAsset>(image.Image);
                var values = image.FrameValues(R, count);
                if (
                    asset == null
                    || n >= values.Length
                    || asset.Layout.Frame(values[n]) is not int frame
                )
                    return new SideView(Task.FromResult(MissingFrameTexture), GridLayout.Single, 0);
                return new SideView(
                    TextureCache.Instance.GetOrCreateAssetTexture(asset),
                    asset.Layout,
                    frame
                );
            }

            case TemplateSide templateSide:
            {
                var template = R.Get<Template>(templateSide.Template);
                var dataset = R.Get<DataSet>(templateSide.Dataset);
                if (template == null || dataset == null)
                    return null;
                var rows = R.GetRows(templateSide.Dataset);
                int frame = rows.FindIndex(r => r.Id == row);
                if (frame < 0)
                    return null;
                return SheetView(
                    new Sheet(
                        $"tpl:{template.SheetKey()}:{dataset.SheetKey()}:{rows.SheetKey()}",
                        template,
                        dataset,
                        rows
                    ),
                    frame
                );
            }

            default:
                return null;
        }
    }

    private const float CmPerInch = 2.54f;

    private const float PixelsPerInch = 100;

    /// <summary>
    /// One side's sheet.
    /// </summary>
    private sealed record Sheet(string Key, Template Template, DataSet DataSet, List<DataRow> Rows);

    /// <summary>
    /// Shows <paramref name="frame"/> of <paramref name="sheet"/>, building it if it isn't cached yet.
    /// </summary>
    private SideView SheetView(Sheet sheet, int frame)
    {
        var (width, height) = sheet.Template.Pixels(PixelsPerInch);
        if (width <= 0 || height <= 0)
            return null;
        var (layout, cellWidth, cellHeight) = SpriteSheetBuilder.Grid(
            width,
            height,
            sheet.Rows.Count
        );
        var texture = TextureCache.Instance.GetOrBuildDerived(
            sheet.Key,
            () =>
            {
                var defs = new List<TextureFactory.TextureDefinition>(sheet.Rows.Count);
                var ctx = new TextureContext
                {
                    DataSet = sheet.DataSet,
                    Dpi = PixelsPerInch,
                    ParentSize = new Vector2(cellWidth, cellHeight),
                };
                foreach (var row in sheet.Rows)
                {
                    ctx.CurrentRow = row;
                    defs.Add(TemplateEngine.GenerateTextureDefinition(sheet.Template, ctx));
                }
                return new SpriteSheetBuilder(
                    TextureFactory,
                    defs,
                    cellWidth,
                    cellHeight,
                    layout.Columns,
                    layout.Rows
                ).Start();
            }
        );
        return new SideView(texture, layout, frame);
    }

    /// <summary>
    /// Shows each side once its texture loads.
    /// </summary>
    private async void ShowSides(SideView front, SideView back)
    {
        int build = _build;
        var frontTexture = front == null ? null : await front.Texture;
        var backTexture = back == null ? null : await back.Texture;
        if (!IsInstanceValid(this) || build != _build)
            return;
        if (front != null)
        {
            (FaceLayout, FaceFrame) = (front.Layout, front.Frame);
            FaceTexture = frontTexture ?? MissingFrameTexture;
            MapFrontTexture();
        }
        if (back != null)
        {
            (BackLayout, BackFrame) = (back.Layout, back.Frame);
            BackTexture = backTexture ?? MissingFrameTexture;
            MapBackTexture();
        }
    }

    private bool _frontTextureGenerated;
    private bool _backTextureGenerated;

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

        SetFrame("front_rect", FaceLayout, FaceFrame);
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
        SetFrame("back_rect", BackLayout, BackFrame);
    }

    private void SetFrame(string face, GridLayout layout, int frame)
    {
        int cols = Math.Max(layout.Columns, 1);
        int rows = Math.Max(layout.Rows, 1);
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

    public TokenShape Shape { get; private set; }

    private TokenType _tokenType;

    /// <summary>
    /// Shown in place of a grid frame that doesn't exist.
    /// </summary>
    private static readonly Texture2D MissingFrameTexture = GD.Load<Texture2D>(
        "res://Textures/missing_frame.png"
    );

    public GridLayout FaceLayout { get; private set; } = GridLayout.Single;
    public int FaceFrame { get; private set; }

    public GridLayout BackLayout { get; private set; } = GridLayout.Single;
    public int BackFrame { get; private set; }

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

    public SideParameters Front { get; init; }

    /// <summary>
    /// What the back shows, or null to show the front on both sides.
    /// </summary>
    public SideParameters Back { get; init; }

    public VcToken.TokenType Type { get; init; }

    /// <summary>
    /// How many cards there are. The max of both sides.
    /// </summary>
    public int CardCount(IRecordReader R) => Math.Max(Front?.Count(R) ?? 0, Back?.Count(R) ?? 0);

    /// <summary>
    /// Card <paramref name="n"/> of this prototype.
    /// </summary>
    public PrototypeItemRef Card(int n, IRecordReader R) =>
        new(n, Front?.Row(n, R) ?? SnowTag.Empty, Back?.Row(n, R) ?? SnowTag.Empty);
}

[JsonName("TokenParameters")]
public sealed record TokenParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Token;
}

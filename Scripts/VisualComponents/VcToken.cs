using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

    /// <summary>
    /// What the front shows.
    /// </summary>
    public Face Front { get; private set; } = Face.Blank;

    /// <summary>
    /// What the back shows.
    /// </summary>
    public Face Back { get; private set; } = Face.Blank;

    /// <summary>
    /// The faces that are currently rendering.
    /// </summary>
    private (Face Front, Face Back)? _loading;

    /// <summary>
    /// The faces to render after <see cref="_loading"/>. Newer faces replace them before they start.
    /// </summary>
    private (Face Front, Face Back)? _next;

    /// <summary>
    /// Called when the front or back changes.
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

        // Idle optimization.
        if (!_flipInProcess)
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

        var prototype = GetPrototype(R);
        // Each view holds its sheet once, so a back that shows the front gets its own view.
        Face? FrontView() => View(p.Front, "f", Card.FrontRow, prototype, R);
        var front = FrontView();
        var back =
            (p.Back == null ? null : View(p.Back, "b", Card.BackRow, prototype, R)) ?? FrontView();
        ShowSides(front ?? Face.Blank, back ?? Face.Blank);
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
        _mainMesh.SetSurfaceOverrideMaterial(SideSurface, SideMaterial);
        ShowFaces();

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
    /// What <paramref name="side"/> shows on this card, or null when it shows nothing.
    /// </summary>
    /// <param name="key">The key to use for the texture cache.</param>
    /// <param name="row">The dataset row this side shows, for a template.</param>
    private Face? View(
        SideParameters side,
        string key,
        SnowTag row,
        Prototype prototype,
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
                var drawing = new Drawing(
                    QuickTemplate.Template with
                    {
                        Width = _width / CmPerInch,
                        Height = _height / CmPerInch,
                    },
                    QuickTemplate.DataSet,
                    distinct
                        .Select(
                            (c, i) =>
                                QuickTemplate.Row(c.Suit, c.Value) with
                                {
                                    Id = new SnowTag(i + 1),
                                }
                        )
                        .ToList()
                );
                var sheet = Sheet.Acquire(
                    $"quick:{prototype.SheetKey()}:{key}",
                    TextureFactory,
                    _ => drawing
                );
                return new Face(
                    sheet,
                    sheet.Frame(new SnowTag(distinct.IndexOf(cards[n % cards.Count]) + 1))
                );
            }

            case ImageSide image:
            {
                var asset = R.Get<ImageAsset>(image.Image);
                var values = image.FrameValues(
                    R,
                    ((PrintedParameters)prototype.Parameters).CardCount(R)
                );
                if (
                    asset == null
                    || n >= values.Length
                    || asset.Layout.Frame(values[n]) is not int frame
                )
                    return new Face(Sheet.Missing, 0);
                var sheet = Sheet.Acquire(
                    $"asset:{asset.CloudPath}:{asset.Layout}",
                    asset.Layout,
                    _ => TextureCache.Instance.GetOrCreateAssetTexture(asset)
                );
                return new Face(sheet, frame);
            }

            case TemplateSide templateSide:
            {
                var sheet = Sheet.Acquire(
                    $"tpl:{templateSide.Template}:{templateSide.Dataset}",
                    TextureFactory,
                    R => new Drawing(
                        R.Get<Template>(templateSide.Template),
                        R.Get<DataSet>(templateSide.Dataset),
                        R.GetRows(templateSide.Dataset)
                    )
                );
                return new Face(sheet, sheet.Frame(row));
            }

            default:
                return null;
        }
    }

    private const float CmPerInch = 2.54f;

    /// <summary>
    /// Shows <paramref name="front"/> and <paramref name="back"/> once their sheets load.
    /// </summary>
    /// <remarks>
    /// When a bunch of rapid updates happen to a token, it will continue showing a texture,
    /// but occasionally replace it with a new one. To achieve this, we keep three sheets in a queue.
    /// <list type="number">
    /// <item>The displayed sheet, which could be outdated.</item>
    /// <item>The sheet that's currently rendering to replace it, which isn't canceled when a change is queued.</item>
    /// <item>The unrendered sheet that's next in line to render when 2 is done, but is replaced when a change is queued.</item>
    /// </list>
    /// This allows the texture to update even when changes are happening rapidly, like with a color picker.
    /// </remarks>
    private void ShowSides(Face front, Face back)
    {
        Release(_next);
        _next = (front, back);
        if (_loading == null)
            LoadNext();
    }

    private async void LoadNext()
    {
        var (front, back) = _next.Value;
        (_loading, _next) = (_next, null);
        await Task.WhenAll(front.Sheet.Load(), back.Sheet.Load());
        if (!IsInstanceValid(this))
            return;
        _loading = null;
        Release((Front, Back));
        Front.Sheet.Changed -= OnSheetChanged;
        Back.Sheet.Changed -= OnSheetChanged;
        (Front, Back) = (front, back);
        Front.Sheet.Changed += OnSheetChanged;
        Back.Sheet.Changed += OnSheetChanged;
        ShowFaces();
        TexturesChanged?.Invoke();
        if (_next != null)
            LoadNext();
    }

    private void OnSheetChanged() => TexturesChanged?.Invoke();

    private static void Release((Face Front, Face Back)? faces)
    {
        if (faces is var (front, back))
        {
            front.Sheet.Release();
            back.Sheet.Release();
        }
    }

    private void ShowFaces()
    {
        _mainMesh.SetSurfaceOverrideMaterial(FrontSurface, Front.Sheet.Material);
        _mainMesh.SetSurfaceOverrideMaterial(BackSurface, Back.Sheet.Material);
        _mainMesh.SetInstanceShaderParameter("front_frame", Front.Frame);
        _mainMesh.SetInstanceShaderParameter("back_frame", Back.Frame);
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what != NotificationPredelete)
            return;
        Release(_next);
        Release(_loading);
        Release((Front, Back));
        Front.Sheet.Changed -= OnSheetChanged;
        Back.Sheet.Changed -= OnSheetChanged;
    }

    private float _height;
    private float _width;
    private float _thickness;

    public TokenShape Shape { get; private set; }

    private TokenType _tokenType;

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

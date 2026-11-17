using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using Vector2 = Godot.Vector2;

namespace Lizzie;

/// <summary>
/// VcPrinted represents any object that is flat on two opposite sides, a geometric prism.
/// This includes cards, tiles, and most wood pieces like meeples.
/// VcPrinted may have a graphic on either of its flat faces, but otherwise has a simple color.
/// </summary>
public partial class VcPrinted : VisualComponentBase
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

    private readonly FaceLoader _faces;

    public VcPrinted() => _faces = new FaceLoader(ShowFaces);

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Printed;
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

    private float _flipRate = 720; //degrees per second
    private float _targetZ;
    private bool _flipInProcess;

    public override bool PlayTransition(
        ComponentPosition s,
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

    protected override void Setup(ComponentParameters parameters, IAtomReader R)
    {
        base.Setup(parameters, R);
        Apply((PrintedParameters)parameters, R);
    }

    /// <summary>Sizes and textures the token from its parameters and the atoms they reference.</summary>
    private void Apply(PrintedParameters p, IAtomReader R)
    {
        if (p.Height <= 0)
            return;
        _height = p.Height / 10f;

        _width = p.Width / 10;

        _thickness = ThicknessOf(p);

        Shape = (TokenShape)p.Shape;

        BuildToken();
        _faces.Load(GetPrototype(R), Item, R);
    }

    public static float ThicknessOf(PrintedParameters p) => Math.Max(p.Thickness / 10f, 0.03f);

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

    private void ShowFaces()
    {
        _mainMesh.SetSurfaceOverrideMaterial(FrontSurface, _faces.Front.Material);
        _mainMesh.SetSurfaceOverrideMaterial(BackSurface, _faces.Back.Material);
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationPredelete)
            _faces.Drop();
    }

    private float _height;
    private float _width;
    private float _thickness;

    public TokenShape Shape { get; private set; }

    public enum TokenType
    {
        Card,
        Token,
        Board,
    }
}

[JsonName("PrintedParameters")]
public sealed record PrintedParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Printed;

    public float Height { get; init; }
    public float Width { get; init; }
    public float Thickness { get; init; }
    public int Shape { get; init; }

    public SideParameters Front { get; init; }

    /// <summary>
    /// What the back shows, or null to show the front on both sides.
    /// </summary>
    public SideParameters Back { get; init; }

    public VcPrinted.TokenType Type { get; init; }

    /// <summary>
    /// Whether spawning makes a deck holding the cards, rather than a loose stack of them.
    /// </summary>
    public bool SpawnDeck { get; init; }

    /// <summary>
    /// How many cards there are, set by the side with biggest count.
    /// </summary>
    public int CardCount(IAtomReader R) =>
        Math.Max(Front?.Faces(R).Count ?? 0, Back?.Faces(R).Count ?? 0);

    /// <summary>
    /// Each side's faces, with an unbounded expression filled to the <see cref="CardCount"/>.
    /// The backs are null when the back shows the front.
    /// </summary>
    private (IReadOnlyList<FaceKey> Fronts, IReadOnlyList<FaceKey> Backs) Faces(IAtomReader R)
    {
        int count = CardCount(R);
        return (Front?.Faces(R, count) ?? [], Back?.Faces(R, count));
    }

    /// <summary>
    /// Whether the sides can pair up.
    /// </summary>
    public bool SidesFit(IAtomReader R)
    {
        var (fronts, backs) = Faces(R);
        return backs == null
            || fronts.Count == backs.Count
            || fronts.Count == 1
            || backs.Count == 1;
    }

    /// <summary>
    /// Every card of this prototype, in order. A side with fewer cards repeats.
    /// </summary>
    public List<PrintedItemRef> Items(IAtomReader R)
    {
        var (fronts, backs) = Faces(R);
        int count = Math.Max(fronts.Count, backs?.Count ?? 0);
        return Enumerable
            .Range(0, count)
            .Select(n => new PrintedItemRef(At(fronts, n), backs == null ? null : At(backs, n)))
            .ToList();
    }

    private static FaceKey At(IReadOnlyList<FaceKey> faces, int n) =>
        faces.Count == 0 ? null : faces[n % faces.Count];

    /// <summary>
    /// Card <paramref name="n"/> of this prototype, or a blank card when it has fewer.
    /// </summary>
    public PrintedItemRef Item(int n, IAtomReader R) =>
        Items(R).ElementAtOrDefault(n) ?? new PrintedItemRef(null, null);
}

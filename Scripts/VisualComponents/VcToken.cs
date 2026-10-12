using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
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
        Quick,
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

        // Quick parameters
        _frontBgColor = p.FrontBgColor;
        _frontField = p.QuickFront;
        _backField = p.QuickBack;

        _frontFontSize = p.FrontFontSize;
        _differentBack = p.DifferentBack;

        _backBgColor = p.BackBgColor;
        _backFontSize = p.BackFontSize;

        //Grid Parameters
        _frontGridImageKey = p.FrontGridImageKey;
        _frontMasterAsset = R.Get<ImageAsset>(_frontGridImageKey);

        _backGridImageKey = p.BackGridImageKey;
        _backMasterAsset = R.Get<ImageAsset>(_backGridImageKey);

        _tokenType = p.Type;

        _frontTemplateRef = p.FrontTemplate;
        _backTemplateRef = p.BackTemplate;
        _datasetRef = p.Dataset;

        _quickCardList = p.QuickCardData;

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

        Build(R);
    }

    private void Build(IRecordReader R)
    {
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
                BuildTemplate(TextureFactory, R);
                break;

            case TokenBuildMode.Nandeck:
                BuildNanDeck();
                break;

            case TokenBuildMode.QuickDeck:
                BuildQuickDeck(TextureFactory);
                break;
        }
    }

    private void BuildToken()
    {
        _mainMesh = GetNode<MeshInstance3D>("SideMesh");

        //FaceSprite = GetNode<Sprite3D>("FrontSprite");
        //BackSprite = GetNode<Sprite3D>("BackSprite");

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

    private void BuildQuick(TextureFactory textureFactory)
    {
        CreateQuickFrontTexture(textureFactory);
        if (_differentBack)
            CreateQuickBackTexture(textureFactory);
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
        int build = ++_gridBuild;
        BuildGridFace(_frontGridFace == null ? null : _frontMasterAsset, build);
        if (_differentBack)
            BuildGridBack(_backGridFace == null ? null : _backMasterAsset, build);
    }

    private async void BuildGridFace(ImageAsset asset, int build)
    {
        var texture = await TextureCache.Instance.GetOrCreateAssetTexture(asset);
        // The card may have been freed, or rebuilt, while the image loaded.
        if (IsInstanceValid(this) && build == _gridBuild)
            ApplyGridFaceTexture(texture ?? MissingFrameTexture);
    }

    private void ApplyGridFaceTexture(Texture2D texture)
    {
        if (!_differentBack)
            _backTextureGenerated = true;
        FaceTexture = texture;
        MapFrontTexture();
    }

    private async void BuildGridBack(ImageAsset asset, int build)
    {
        var texture = await TextureCache.Instance.GetOrCreateAssetTexture(asset);
        if (IsInstanceValid(this) && build == _gridBuild)
            ApplyGridBackTexture(texture ?? MissingFrameTexture);
    }

    private void ApplyGridBackTexture(Texture2D texture)
    {
        BackTexture = texture;
        MapBackTexture();
    }

    private void ApplyDerivedSheet(
        Texture2D front,
        Texture2D back,
        int faceHframes,
        int faceVframes,
        int faceFrame,
        int backHframes,
        int backVframes,
        int backFrame
    )
    {
        _faceHframes = faceHframes;
        _faceVframes = faceVframes;
        _faceFrame = faceFrame;
        _backHframes = backHframes;
        _backVframes = backVframes;
        _backFrame = backFrame;
        FaceTexture = front;
        BackTexture = back;
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
        int n = rows.Count;
        if (n == 0)
            return;

        int faceFrame = rows.FindIndex(r => r.Id == DataSetRowId);
        if (faceFrame < 0)
            return;

        var (cellW, cellH) = ft.Pixels(10 * BASE_DPI);
        if (cellW <= 0 || cellH <= 0)
            return;

        var (backCellW, backCellH) = bt?.Pixels(10 * BASE_DPI) ?? (cellW, cellH);

        int hframes = (int)Math.Ceiling(Math.Sqrt(n));
        int vframes = (int)Math.Ceiling((double)n / hframes);

        string rowsKey = rows.SheetKey();
        string frontKey = $"tpl:{ft.SheetKey()}:{ds.SheetKey()}:{rowsKey}";
        string backKey = bt != null ? $"tpl:{bt.SheetKey()}:{ds.SheetKey()}:{rowsKey}" : null;

        Texture2D front = null;
        Texture2D back = null;
        bool backReady = bt == null;

        void Apply()
        {
            if (front == null || !backReady)
                return;
            var effectiveBack = bt == null ? front : back;
            if (effectiveBack == null)
                return;

            ApplyDerivedSheet(
                front,
                effectiveBack,
                hframes,
                vframes,
                faceFrame,
                hframes,
                vframes,
                faceFrame
            );
        }

        bool weBuildFront = TextureCache.Instance.RequestDerived(
            frontKey,
            t =>
            {
                front = t;
                Apply();
            }
        );

        bool weBuildBack = false;
        if (bt != null)
        {
            weBuildBack = TextureCache.Instance.RequestDerived(
                backKey,
                t =>
                {
                    back = t;
                    backReady = true;
                    Apply();
                }
            );
        }

        if (weBuildFront)
            StartTemplateSheetBuild(
                textureFactory,
                ft,
                ds,
                rows,
                cellW,
                cellH,
                hframes,
                vframes,
                frontKey
            );

        if (weBuildBack)
            StartTemplateSheetBuild(
                textureFactory,
                bt,
                ds,
                rows,
                backCellW,
                backCellH,
                hframes,
                vframes,
                backKey
            );
    }

    private const float BASE_DPI = 10f;

    private static void StartTemplateSheetBuild(
        TextureFactory factory,
        Template template,
        DataSet dataset,
        List<DataRow> rows,
        int cellW,
        int cellH,
        int hframes,
        int vframes,
        string cacheKey
    )
    {
        var defs = new List<TextureFactory.TextureDefinition>(rows.Count);
        var ctx = new TextureContext
        {
            DataSet = dataset,
            Dpi = 10 * BASE_DPI,
            ParentSize = new Vector2(cellW, cellH),
        };
        foreach (var row in rows)
        {
            ctx.CurrentRow = row;
            defs.Add(TemplateEngine.GenerateTextureDefinition(template, ctx));
        }
        new SpriteSheetBuilder(
            factory,
            defs,
            cellW,
            cellH,
            hframes,
            vframes,
            tex => TextureCache.Instance.PutDerived(cacheKey, tex)
        ).Start();
    }

    private ImmutableArray<QuickCardData> _quickCardList = new();

    private void BuildQuickDeck(TextureFactory textureFactory)
    {
        int faceFrame = DataSetRowIndex;
        if (faceFrame < 0)
            return;

        var cards = ExpandQuickCardList(_quickCardList);
        int n = cards.Length;
        if (n == 0)
            return;

        int hframes = (int)Math.Ceiling(Math.Sqrt(n));
        int vframes = (int)Math.Ceiling((double)n / hframes);

        ComputeCellSize(_height, _width, out var cellW, out var cellH);

        bool singleBack = AllBacksIdentical(cards);
        int backFrameIndex = singleBack ? 0 : faceFrame;
        int backH = singleBack ? 1 : hframes;
        int backV = singleBack ? 1 : vframes;

        string frontKey = QuickDeckSheetKey(cards, cellW, cellH, hframes, vframes, "f");
        string backKey = singleBack
            ? QuickDeckSingleBackKey(cards[0], cellW, cellH)
            : QuickDeckSheetKey(cards, cellW, cellH, hframes, vframes, "b");

        ApplySheetWhenReady(
            textureFactory,
            cards,
            frontKey,
            backKey,
            cellW,
            cellH,
            hframes,
            vframes,
            backH,
            backV,
            faceFrame,
            backFrameIndex,
            singleBack
        );
    }

    private void ApplySheetWhenReady(
        TextureFactory textureFactory,
        ImmutableArray<QuickCardData> cards,
        string frontKey,
        string backKey,
        int cellW,
        int cellH,
        int hframes,
        int vframes,
        int backH,
        int backV,
        int faceFrame,
        int backFrameIndex,
        bool singleBack
    )
    {
        Texture2D front = null;
        Texture2D back = null;

        void Apply()
        {
            if (front == null || back == null)
                return;
            ApplyDerivedSheet(
                front,
                back,
                hframes,
                vframes,
                faceFrame,
                backH,
                backV,
                backFrameIndex
            );
        }

        bool weBuildFront = TextureCache.Instance.RequestDerived(
            frontKey,
            t =>
            {
                front = t;
                Apply();
            }
        );
        bool weBuildBack = TextureCache.Instance.RequestDerived(
            backKey,
            t =>
            {
                back = t;
                Apply();
            }
        );

        if (weBuildFront)
        {
            var defs = new List<TextureFactory.TextureDefinition>(cards.Length);
            foreach (var card in cards)
            {
                defs.Add(
                    BuildQuickTextureDefinition(
                        card.BackgroundColor,
                        new QuickTextureField
                        {
                            Caption = card.Caption,
                            FaceType = TextureFactory.TextureObjectType.Text,
                            ForegroundColor = Colors.Black,
                            Quantity = 1,
                        },
                        _height,
                        _width
                    )
                );
            }
            new SpriteSheetBuilder(
                textureFactory,
                defs,
                cellW,
                cellH,
                hframes,
                vframes,
                tex => TextureCache.Instance.PutDerived(frontKey, tex)
            ).Start();
        }

        if (weBuildBack)
        {
            var defs = new List<TextureFactory.TextureDefinition>();
            var backCards = singleBack ? [cards[0]] : cards;
            foreach (var card in backCards)
            {
                defs.Add(
                    BuildQuickTextureDefinition(
                        card.CardBackColor,
                        new QuickTextureField
                        {
                            Caption = card.CardBackValue,
                            FaceType = TextureFactory.TextureObjectType.Text,
                            ForegroundColor = Colors.Black,
                            Quantity = 1,
                        },
                        _height,
                        _width
                    )
                );
            }
            new SpriteSheetBuilder(
                textureFactory,
                defs,
                cellW,
                cellH,
                backH,
                backV,
                tex => TextureCache.Instance.PutDerived(backKey, tex)
            ).Start();
        }
    }

    private static ImmutableArray<QuickCardData> ExpandQuickCardList(
        ImmutableArray<QuickCardData> source
    )
    {
        var cards = ImmutableArray.CreateBuilder<QuickCardData>();
        foreach (var q in source)
        {
            foreach (var v in Utility.ParseValueRanges(q.Caption))
            {
                cards.Add(
                    new QuickCardData
                    {
                        Caption = v,
                        BackgroundColor = q.BackgroundColor,
                        CardBackValue = q.CardBackValue,
                        CardBackColor = q.CardBackColor,
                    }
                );
            }
        }
        return cards.ToImmutable();
    }

    private static bool AllBacksIdentical(ImmutableArray<QuickCardData> cards)
    {
        if (cards.Length <= 1)
            return true;
        var first = cards[0];
        for (int i = 1; i < cards.Length; i++)
        {
            if (
                cards[i].CardBackValue != first.CardBackValue
                || cards[i].CardBackColor != first.CardBackColor
            )
                return false;
        }
        return true;
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

    private static string QuickDeckSheetKey(
        ImmutableArray<QuickCardData> cards,
        int cellW,
        int cellH,
        int hframes,
        int vframes,
        string side
    )
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("qd-").Append(side).Append(':');
        sb.Append(cellW).Append('x').Append(cellH).Append(':');
        sb.Append(hframes).Append('x').Append(vframes).Append(':');
        foreach (var c in cards)
        {
            if (side == "f")
                sb.Append(c.Caption).Append('|').Append(c.BackgroundColor.ToHtml()).Append(';');
            else
                sb.Append(c.CardBackValue).Append('|').Append(c.CardBackColor.ToHtml()).Append(';');
        }
        return sb.ToString();
    }

    private static string QuickDeckSingleBackKey(QuickCardData card, int cellW, int cellH)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("qd-bsingle:");
        sb.Append(cellW).Append('x').Append(cellH).Append(':');
        sb.Append(card.CardBackValue).Append('|').Append(card.CardBackColor.ToHtml());
        return sb.ToString();
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

    private void CreateQuickFrontTexture(TextureFactory textureFactory)
    {
        var key = QuickSingleKey(_frontBgColor, _frontField, side: "f");
        bool weBuild = TextureCache.Instance.RequestDerived(
            key,
            tex =>
            {
                if (tex is ImageTexture it)
                    FinalizeFrontTexture(it);
            }
        );
        if (!weBuild)
            return;

        var td = CreateQuickTextureDefinition(_frontBgColor, _frontField);
        textureFactory.GenerateTexture(td, t => TextureCache.Instance.PutDerived(key, t));
    }

    private TextureFactory.TextureDefinition CreateQuickTextureDefinition(
        Color bgColor,
        QuickTextureField qtf
    )
    {
        return BuildQuickTextureDefinition(bgColor, qtf, _height, _width);
    }

    private static TextureFactory.TextureDefinition BuildQuickTextureDefinition(
        Color bgColor,
        QuickTextureField qtf,
        float height,
        float width
    )
    {
        int sH = 256;
        int sW = 256;

        if (height <= 0 || width <= 0)
            return new TextureFactory.TextureDefinition();
        if (height > width)
        {
            sW = (int)(width * 256 / height);
        }
        else
        {
            sH = (int)(height * 256 / width);
        }

        var td = new TextureFactory.TextureDefinition
        {
            BackgroundColor = bgColor,
            Height = sH,
            Width = sW,
        };

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

        MapFrontTexture();
    }

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

    private void CreateQuickBackTexture(TextureFactory textureFactory)
    {
        var key = QuickSingleKey(_backBgColor, _backField, side: "b");
        bool weBuild = TextureCache.Instance.RequestDerived(
            key,
            tex =>
            {
                if (tex is ImageTexture it)
                    FinalizeBackTexture(it);
            }
        );
        if (!weBuild)
            return;

        var td = CreateQuickTextureDefinition(_backBgColor, _backField);
        textureFactory.GenerateTexture(td, t => TextureCache.Instance.PutDerived(key, t));
    }

    private string QuickSingleKey(Color bgColor, QuickTextureField qtf, string side)
    {
        var caption = qtf?.Caption ?? string.Empty;
        var fg = (qtf?.ForegroundColor ?? Colors.Black).ToHtml();
        var type = (int)(qtf?.FaceType ?? TextureFactory.TextureObjectType.Text);
        var qty = qtf?.Quantity ?? 1;
        return $"qs-{side}:{(int)(_width * 256)}x{(int)(_height * 256)}:{bgColor.ToHtml()}:{type}:{qty}:{fg}:{caption}";
    }

    private void FinalizeBackTexture(ImageTexture t)
    {
        _backTextureGenerated = true;

        BackTexture = t;

        TextureReady = _frontTextureGenerated && _backTextureGenerated;

        MapBackTexture();
    }

    private float _height;
    private float _width;
    private float _thickness;
    private string _frontImage;
    private string _backImage;

    public TokenShape Shape { get; private set; }
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
    private ImageAsset _frontMasterAsset;
    private ImageAsset _backMasterAsset;
    private SnowTag _frontGridImageKey;
    private SnowTag _backGridImageKey;

    private (GridLayout Layout, int Frame)? _frontGridFace;
    private (GridLayout Layout, int Frame)? _backGridFace;

    /// <summary>
    /// Counts grid builds to throw out stale load callbacks.
    /// </summary>
    private int _gridBuild;

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

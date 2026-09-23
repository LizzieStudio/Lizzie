using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    public Image FaceSprite => FaceTexture.GetImage();

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

    public Image BackSprite => BackTexture.GetImage();

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

    public override Effect[] ProcessCommand(VisualCommand command)
    {
        if (command == VisualCommand.Flip)
            return [BuildFlip()];

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

    private ComponentEffect BuildFlip()
    {
        bool targetFaceUp = RotationDegrees.Z >= 90;

        var e = ComponentEffect.Capture(this);
        e.State.Rotation = new Vector3(
            e.State.Rotation.X,
            e.State.Rotation.Y,
            Mathf.DegToRad(targetFaceUp ? 0f : 180f)
        );

        return e;
    }

    public override void AnimateFlip(Vector3 targetRotation)
    {
        _flipInProcess = true;
        _targetZ = Mathf.RadToDeg(targetRotation.Z);
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

    protected override bool Setup(ComponentParameters parameters, IRecordReader R)
    {
        base.Setup(parameters, R);
        return Apply((PrintedParameters)parameters, R);
    }

    /// <summary>Sizes and textures the token from its parameters and the records they reference.</summary>
    private bool Apply(PrintedParameters p, IRecordReader R)
    {
        if (p.Height <= 0)
            return false;
        _height = p.Height / 10f;

        _width = p.Width / 10;

        _thickness = Math.Max(p.Thickness / 10f, 0.03f);

        _frontImage = p.FrontImage;
        _backImage = p.BackImage;

        _shape = p.Shape;
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
        _frontMasterAsset = R.Get<Asset>(_frontGridImageKey);

        _backGridImageKey = p.BackGridImageKey;
        _backMasterAsset = R.Get<Asset>(_backGridImageKey);

        _gridRows = p.GridRows;
        _gridCols = p.GridCols;
        _gridCount = p.GridCount;
        _gridSingleBack = p.GridSingleBack;

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
            int cols = Math.Max(_gridCols, 1);
            int rows = Math.Max(_gridRows, 1);
            int idx = Math.Max(DataSetRowIndex, 0);
            _faceHframes = cols;
            _faceVframes = rows;
            _faceFrame = idx;
            if (_gridSingleBack)
            {
                _backHframes = 1;
                _backVframes = 1;
                _backFrame = 0;
            }
            else
            {
                _backHframes = cols;
                _backVframes = rows;
                _backFrame = idx;
            }
        }

        Build(R);

        return true;
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

    public float Height => _height;
    public float Width => _width;

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
            ApplyGridFaceTexture(new ImageTexture());
        }
        else
        {
            _ = BuildGridFace(_frontMasterAsset);
        }

        if (_differentBack)
        {
            if (_backMasterAsset == null)
            {
                ApplyGridBackTexture(new ImageTexture());
            }
            else
            {
                _ = BuildGridBack(_backMasterAsset);
            }
        }
    }

    private async Task BuildGridFace(Asset asset)
    {
        ApplyGridFaceTexture(await TextureCache.Instance.GetOrCreateAssetTexture(asset));
    }

    private void ApplyGridFaceTexture(Texture2D texture)
    {
        if (!_differentBack)
            _backTextureGenerated = true;
        FaceTexture = texture;
        MapFrontTexture();
    }

    private async Task BuildGridBack(Asset asset)
    {
        ApplyGridBackTexture(await TextureCache.Instance.GetOrCreateAssetTexture(asset));
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
        TextureChanged = true;
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

        int cellW = (int)(ft.Width * 10 * BASE_DPI);
        int cellH = (int)(ft.Height * 10 * BASE_DPI);
        if (cellW <= 0 || cellH <= 0)
            return;

        int backCellW = bt != null ? (int)(bt.Width * 10 * BASE_DPI) : cellW;
        int backCellH = bt != null ? (int)(bt.Height * 10 * BASE_DPI) : cellH;

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

    public const float BASE_DPI = 10f;

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

        string frontKey = QuickDeckSheetKey(cards, _shape, cellW, cellH, hframes, vframes, "f");
        string backKey = singleBack
            ? QuickDeckSingleBackKey(cards[0], _shape, cellW, cellH)
            : QuickDeckSheetKey(cards, _shape, cellW, cellH, hframes, vframes, "b");

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
                        _width,
                        _shape
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
                        _width,
                        _shape
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

    public static void ComputeCellSize(float height, float width, out int cellW, out int cellH)
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

    public static string QuickDeckSheetKey(
        ImmutableArray<QuickCardData> cards,
        int shape,
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
        sb.Append(shape).Append(':');
        foreach (var c in cards)
        {
            if (side == "f")
                sb.Append(c.Caption).Append('|').Append(c.BackgroundColor.ToHtml()).Append(';');
            else
                sb.Append(c.CardBackValue).Append('|').Append(c.CardBackColor.ToHtml()).Append(';');
        }
        return sb.ToString();
    }

    public static string QuickDeckSingleBackKey(QuickCardData card, int shape, int cellW, int cellH)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("qd-bsingle:");
        sb.Append(cellW).Append('x').Append(cellH).Append(':');
        sb.Append(shape).Append(':');
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
        return BuildQuickTextureDefinition(bgColor, qtf, _height, _width, _shape);
    }

    public static TextureFactory.TextureDefinition BuildQuickTextureDefinition(
        Color bgColor,
        QuickTextureField qtf,
        float height,
        float width,
        int shape
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

        switch (shape)
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

        ApplyUvOffset(_frontMaterial, _faceHframes, _faceVframes, _faceFrame);

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

        if (!_gridSingleBack)
        {
            ApplyUvOffset(_backMaterial, _backHframes, _backVframes, _backFrame);
        }
    }

    private static void ApplyUvOffset(StandardMaterial3D mat, int hframes, int vframes, int frame)
    {
        int cols = Math.Max(hframes, 1);
        int rows = Math.Max(vframes, 1);
        int col = frame % cols;
        int row = frame / cols;
        mat.Uv1Scale = new Vector3(1f / cols, 1f / rows, 1f);
        mat.Uv1Offset = new Vector3((float)col / cols, (float)row / rows, 0f);
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
        return $"qs-{side}:{_shape}:{(int)(_width * 256)}x{(int)(_height * 256)}:{bgColor.ToHtml()}:{type}:{qty}:{fg}:{caption}";
    }

    private void FinalizeBackTexture(ImageTexture t)
    {
        _backTextureGenerated = true;

        BackTexture = t;

        TextureReady = _frontTextureGenerated && _backTextureGenerated;
        TextureChanged = true;

        MapBackTexture();
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
    private Asset _frontMasterAsset;
    private Asset _backMasterAsset;
    private SnowTag _frontGridImageKey;
    private SnowTag _backGridImageKey;

    private int _gridRows;
    private int _gridCols;

    private int _gridCount;
    private bool _gridSingleBack;

    //private int _gridIndex;

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
    public int GridRows { get; init; }
    public int GridCols { get; init; }
    public int GridCount { get; init; }
    public bool GridSingleBack { get; init; }

    public SnowTag FrontTemplate { get; init; }
    public SnowTag BackTemplate { get; init; }
    public SnowTag Dataset { get; init; }
}

public sealed record TokenParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Token;
}

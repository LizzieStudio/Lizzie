using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

            /*
            if (value != null)
            {
                var image = value.GetImage();
                if (image != null)
                {
                    image.SavePng("c:/winwam5/facetest.png");
                }
            }
            */

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

    private TransformEffect BuildFlip()
    {
        bool targetFaceUp = RotationDegrees.Z >= 90;

        var t = TransformEffect.Capture(this);
        t.Rotation = new Vector3(
            t.Rotation.X,
            t.Rotation.Y,
            Mathf.DegToRad(targetFaceUp ? 0f : 180f)
        );

        return t;
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

    private bool _buildRequired;

    public override bool Setup(
        ComponentParameters parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        base.Setup(parameters, dataSetRow, textureFactory);

        // VcToken renders both standalone tokens and the cards inside a deck, so the
        // parameters may be either TokenParameters or DeckParameters — both derive from
        // PrintedParameters.
        var p = (PrintedParameters)parameters;

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

        _backGridImageKey = p.BackGridImageKey;
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

        _gridRows = p.GridRows;
        _gridCols = p.GridCols;
        _gridCount = p.GridCount;
        _gridSingleBack = p.GridSingleBack;

        _tokenType = p.Type;

        _frontTemplateRef = p.FrontTemplate;
        _backTemplateRef = p.BackTemplate;
        _datasetRef = p.Dataset;
        if (string.IsNullOrWhiteSpace(DataSetRow))
            DataSetRow = p.CardReference;

        _quickCardList = p.QuickCardData ?? new();

        // Face-frame indices are render-time state (never persisted); default them and, in
        // grid mode, derive them from the grid dimensions and the current row index.
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
            int.TryParse(DataSetRow, out var idx);
            idx = Math.Max(idx, 0);
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

        Build();

        return true;
    }

    public override void Build()
    {
        if (!IsNodeReady())
        {
            _buildRequired = true;
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

        _frontMasterSprite = TextureCache.Instance.GetOrCreateAssetTexture(asset);

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

        _backMasterSprite = TextureCache.Instance.GetOrCreateAssetTexture(asset);

        ApplyGridBackTexture();
    }

    private void ApplyGridBackTexture()
    {
        BackTexture = _backMasterSprite;
        MapBackTexture();
    }

    private SnowportId _frontTemplateRef;
    private SnowportId _backTemplateRef;
    private SnowportId _datasetRef;

    private void BuildTemplate(TextureFactory textureFactory)
    {
        _differentBack = true;

        if (_height <= 0 || _width <= 0)
            return;

        if (_frontTemplateRef == SnowportId.Empty)
            return;

        var curProj = ProjectService.Instance.CurrentProject;
        var ft = curProj.GetTemplate(_frontTemplateRef);
        var ds = curProj.GetDataset(_datasetRef);

        var bt = curProj.GetTemplate(_backTemplateRef);
        if (bt is null || bt.TemplateRef == SnowportId.Empty)
            bt = null;

        if (ft is null || ds is null)
            return;

        var rows = ds.Rows.Select(r => r.Key).ToList();
        int n = rows.Count;
        if (n == 0)
            return;

        int faceFrame = rows.IndexOf(DataSetRow);
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

        string frontKey = $"tpl:{ft.SheetKey()}:{ds.SheetKey()}";
        string backKey = bt != null ? $"tpl:{bt.SheetKey()}:{ds.SheetKey()}" : null;

        Texture2D front = null;
        Texture2D back = null;
        bool backReady = bt == null; // no back template ⇒ back reuses the front

        void Apply()
        {
            if (front == null || !backReady)
                return;
            var effectiveBack = bt == null ? front : back;
            if (effectiveBack == null)
                return;

            _frontMasterSprite = front;
            _backMasterSprite = effectiveBack;
            _faceHframes = hframes;
            _faceVframes = vframes;
            _faceFrame = faceFrame;
            _backHframes = hframes;
            _backVframes = vframes;
            _backFrame = faceFrame;
            FaceTexture = front;
            BackTexture = effectiveBack;
            _frontTextureGenerated = true;
            _backTextureGenerated = true;
            TextureChanged = true;
            MapFrontTexture();
            MapBackTexture();
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
        List<string> rows,
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
            ctx.CurrentRowName = row;
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

    private List<QuickCardData> _quickCardList = new();

    private void BuildQuickDeck(TextureFactory textureFactory)
    {
        int.TryParse(DataSetRow, out var r);
        if (r == 0)
            return;

        var cards = ExpandQuickCardList(_quickCardList);
        int n = cards.Count;
        if (n == 0)
            return;

        int hframes = (int)Math.Ceiling(Math.Sqrt(n));
        int vframes = (int)Math.Ceiling((double)n / hframes);

        ComputeCellSize(_height, _width, out var cellW, out var cellH);

        bool singleBack = AllBacksIdentical(cards);
        int faceFrame = r - 1;
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
        List<QuickCardData> cards,
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
            _frontMasterSprite = front;
            _backMasterSprite = back;
            _faceHframes = hframes;
            _faceVframes = vframes;
            _faceFrame = faceFrame;
            _backHframes = backH;
            _backVframes = backV;
            _backFrame = backFrameIndex;
            FaceTexture = front;
            BackTexture = back;
            _frontTextureGenerated = true;
            _backTextureGenerated = true;
            TextureChanged = true;
            MapFrontTexture();
            MapBackTexture();
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
            var defs = new List<TextureFactory.TextureDefinition>(cards.Count);
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
            var backCards = singleBack ? new List<QuickCardData> { cards[0] } : cards;
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

    private static List<QuickCardData> ExpandQuickCardList(List<QuickCardData> source)
    {
        var cards = new List<QuickCardData>();
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
        return cards;
    }

    private static bool AllBacksIdentical(List<QuickCardData> cards)
    {
        if (cards.Count <= 1)
            return true;
        var first = cards[0];
        for (int i = 1; i < cards.Count; i++)
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
        List<QuickCardData> cards,
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

public class TokenSize
{
    public float Height { get; set; }
    public float Width { get; set; }
}

public abstract class PrintedParameters : ComponentParameters
{
    public float Height { get; set; }
    public float Width { get; set; }
    public float Thickness { get; set; }
    public int Shape { get; set; }
    public VcToken.TokenBuildMode Mode { get; set; }
    public bool DifferentBack { get; set; }

    public string FrontImage { get; set; } = "";
    public string BackImage { get; set; } = "";

    public Color FrontBgColor { get; set; } = Colors.Black;
    public Color BackBgColor { get; set; } = Colors.Black;

    public QuickTextureField QuickFront { get; set; } = new();
    public QuickTextureField QuickBack { get; set; } = new();

    public int FrontFontSize { get; set; }
    public int BackFontSize { get; set; }

    public VcToken.TokenType Type { get; set; }

    public List<QuickCardData> QuickCardData { get; set; } = new();

    public string FrontGridImageKey { get; set; } = "";
    public string BackGridImageKey { get; set; } = "";
    public int GridRows { get; set; }
    public int GridCols { get; set; }
    public int GridCount { get; set; }
    public bool GridSingleBack { get; set; }

    public SnowportId FrontTemplate { get; set; }
    public SnowportId BackTemplate { get; set; }
    public SnowportId Dataset { get; set; }
    public string CardReference { get; set; } = "";
}

public sealed class TokenParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Token;
}

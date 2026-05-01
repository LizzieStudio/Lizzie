using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Godot;

public partial class VcDeck : VisualComponentGroup
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
        ComponentType = VisualComponentType.Deck;

        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        _frontSprite = GetNode<Sprite3D>("FrontSprite");
        _backSprite = GetNode<Sprite3D>("BackSprite");

        CanAcceptDrop = true;
    }

    public override void _Process(double delta)
    {
        if (!TextureReady)
            UpdateDeckSprites();

        CheckForSpriteUpdate();

        if (_flipInProcess)
        {
            ProcessFlip(delta);
        }

        if (_spriteUpdateCountdown > 0)
        {
            _spriteUpdateCountdown--;
            if (_spriteUpdateCountdown == 0)
                UpdateDeckSprites();
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

        SetRotationDegrees(new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ));
    }

    private CommandResponse DrawCards(int count)
    {
        count = Math.Min(count, Children.Count);

        Guid[] cards;
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

        //splay
        var basePos = Position;

        for (int i = 0; i < cards.Length; i++)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(cards[i]);

            if (comp == null)
                continue;

            if (comp is VcToken vcf)
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

            //tween to handle movement
            //var cardTween = GetTree().CreateTween();

            comp.Location = ComponentLocation.Board;

            float deltaX = Position.X + (_width * (1.5f + i));

            /*
            cardTween.TweenProperty(cards[i], "visible", true, 0.01);
            cardTween.TweenProperty(
                cards[i],
                "position",
                new Vector3(deltaX, Position.Y, Position.Z),
                0.2f
            );
            */
            comp.SetPosition(new Vector3(deltaX, Position.Y, Position.Z));

            comp.ZOrder = ZOrder + i + 1;
        }

        var c = new Change
        {
            Action = Change.ChangeType.Transform,
            Begin = Transform,
            End = Transform,
            Component = this,
        };

        UpdateDeckSprites();

        return new CommandResponse(true, c);
    }

    public override void SpawnBuild(
        Guid prototypeRef,
        VcSyncDto syncDto,
        TextureFactory textureFactory
    )
    {
        if (ProjectService.Instance.CurrentProject == null)
            return;

        if (
            !ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(
                prototypeRef,
                out var proto
            )
        )
        {
            return;
        }

        syncDto.ApplyToComponent(this);
        BuildInternal(proto.Parameters, textureFactory, false);
    }

    public override bool Setup(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        return BuildInternal(parameters, textureFactory, true);
    }

    private bool BuildInternal(
        Dictionary<string, object> parameters,
        TextureFactory textureFactory,
        bool spawnCards
    )
    {
        base.Setup(parameters, textureFactory);

        _frontSprite = GetNode<Sprite3D>("FrontSprite");
        _backSprite = GetNode<Sprite3D>("BackSprite");

        _frontView = GetNode<TokenTextureSubViewport>("FrontViewport");
        _backView = GetNode<TokenTextureSubViewport>("BackViewport");

        if (!InitializeParameters(parameters))
            return false;

        if (spawnCards)
        {
            switch (_mode)
            {
                case VcToken.TokenBuildMode.QuickDeck:
                    BuildQuick(parameters, textureFactory);
                    break;

                case VcToken.TokenBuildMode.Template:
                    BuildTemplate(parameters, textureFactory);
                    break;

                case VcToken.TokenBuildMode.Grid:
                    BuildGrid(parameters, textureFactory);
                    break;
            }
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
                hf.Points = CalcHexFlatVertices();
                ShapeProfiles.Add(new OffsetShape2D(hf));
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        _frontView.Ready += RegisterInitializedViews;
        _backView.Ready += RegisterInitializedViews;

        //place all cards below the table so they get rendered;

        int h = (int)Math.Floor(_height * 20);
        int w = (int)Math.Floor(_width * 20);

        _frontTextureReady = false;
        _backTextureReady = false;

        UpdateDeckSprites();
        SyncRequired = true;
        return true;
    }

    public override bool Refresh(TextureFactory textureFactory)
    {
        /*
        var fTemplateParam = Utility.GetParam<string>(parameters, "FrontTemplate");
        var bTemplateParam = Utility.GetParam<string>(parameters, "BackTemplate");

        var datasetParam = Utility.GetParam<string>(parameters, "Dataset");
        var dataset = ProjectService.Instance.CurrentProject.Datasets[datasetParam];

        RefreshTemplateCards(
            fTemplateParam,
            bTemplateParam,
            datasetParam,
            dataset.Rows.Count,
            textureFactory
        );
        */

        foreach (var c in Children)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(c);
            if (comp is VcToken card)
                card.Refresh(textureFactory);
        }

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
        arr[4] = new Vector2(x, -y);
        arr[5] = new Vector2(x, y);

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

        if (_quickCardList == null)
            _quickCardList = new();

        CreateQuickCards(textureFactory);
    }

    private void BuildTemplate(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        var fTemplateParam = Utility.GetParam<string>(parameters, "FrontTemplate");
        var bTemplateParam = Utility.GetParam<string>(parameters, "BackTemplate");
        var datasetParam = Utility.GetParam<string>(parameters, "Dataset");

        var dataset = ProjectService.Instance.CurrentProject.Datasets[datasetParam];

        CreateTemplateCards(fTemplateParam, bTemplateParam, dataset, textureFactory);
    }

    private void CreateTemplateCards(
        string frontTemplate,
        string backTemplate,
        DataSet dataset,
        TextureFactory textureFactory
    )
    {
        Clear();

        foreach (var kv in dataset.Rows)
        {
            var card = (VcToken)_templateCard.Duplicate();
            card.ComponentType = VisualComponentType.Token;

            CreateTemplateCard(
                frontTemplate,
                backTemplate,
                dataset.Name,
                card,
                kv.Key,
                textureFactory
            );

            CreateAndAddChildComponent(card);
        }
    }

    private void RefreshTemplateCards(
        string frontTemplate,
        string backTemplate,
        string dataset,
        int cardCount,
        TextureFactory textureFactory
    )
    {
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children.ElementAt(i);
            var comp = ProjectService.Instance.GameObjects.GetComponent(c);
            if (comp is VcToken card)
            {
                CreateTemplateCard(
                    frontTemplate,
                    backTemplate,
                    dataset,
                    card,
                    comp.DataSetRow,
                    textureFactory
                );
            }
        }
    }

    private void CreateCustomFrontTexture()
    {
        if (!File.Exists(_frontImage))
            return;

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
        if (!File.Exists(_backImage))
            return;

        _backView.SetViewPortMode(TokenTextureSubViewport.ShapeViewportMode.Texture);
        _backView.SetShape((TokenTextureSubViewport.TokenShape)_shape);
        var t = _backView.GetTexture();

        float pixelSize = Utility.PixelSize(t.GetSize());
        _backSprite.PixelSize = pixelSize;
        _backView.SetTexture(LoadTexture(_backImage));

        _backSprite.Texture = _backView.GetTexture();
    }

    private bool InitializeParameters(
        System.Collections.Generic.Dictionary<string, object> parameters
    )
    {
        var h = Utility.GetParam<float>(parameters, "Height");
        if (h <= 0)
            return false;
        _height = h / 10f;

        var w = Utility.GetParam<float>(parameters, "Width");
        _width = w / 10f;

        _mode = Utility.GetParam<VcToken.TokenBuildMode>(parameters, "Mode");

        var scene = ResourceLoader.Load<PackedScene>(_templateCardPath).Instantiate();

        if (scene is not VcToken token)
            return false;

        _templateCard = token;

        return true;
    }

    private void CreateQuickCards(TextureFactory textureFactory)
    {
        Clear();

        int cardNum = 1;

        foreach (var q in _quickCardList)
        {
            var values = Utility.ParseValueRanges(q.Caption);

            foreach (var v in values)
            {
                var c = CreateQuickCard(
                    v,
                    q.BackgroundColor,
                    q.CardBackValue,
                    q.CardBackColor,
                    cardNum,
                    textureFactory
                );

                CreateAndAddChildComponent(c);
                cardNum++;
            }
        }
    }

    private VcToken CreateQuickCard(
        string faceCaption,
        Color faceColor,
        string backCaption,
        Color backColor,
        int cardNum,
        TextureFactory textureFactory
    )
    {
        var card = (VcToken)_templateCard.Duplicate();
        card.ComponentType = VisualComponentType.Token;

        card.Parent = Reference;
        card.PrototypeRef = PrototypeRef;

        card.Setup(PrototypeRef, cardNum.ToString(), textureFactory);

        return card;
    }

    private void CreateTemplateCard(
        string frontTemplate,
        string backTemplate,
        string dataset,
        VcToken card,
        string cardRef,
        TextureFactory textureFactory
    )
    {
        card.PrototypeRef = PrototypeRef;
        card.DataSetRow = cardRef;
        card.Parent = Reference;

        card.Setup(PrototypeRef, cardRef, textureFactory);
    }

    #region Grid Cards

    private Texture2D _frontMasterSprite;
    private Texture2D _backMasterSprite;
    private int _gridRows;
    private int _gridCols;
    private int _gridCount;
    private bool _gridSingleBack;

    private void BuildGrid(Dictionary<string, object> parameters, TextureFactory textureFactory)
    {
        //Grid Parameters
        _frontMasterSprite = Utility.GetParam<Texture2D>(parameters, "FrontMasterSprite");
        _backMasterSprite = Utility.GetParam<Texture2D>(parameters, "BackMasterSprite");

        _gridRows = Utility.GetParam<int>(parameters, "GridRows");
        _gridCols = Utility.GetParam<int>(parameters, "GridCols");
        _gridCount = Utility.GetParam<int>(parameters, "GridCount");
        _gridSingleBack = Utility.GetParam<bool>(parameters, "GridSingleBack");

        CreateGridCards(textureFactory);
    }

    private void CreateGridCards(TextureFactory textureFactory)
    {
        Clear();

        for (int i = 0; i < _gridCount; i++)
        {
            var c = CreateGridCard(i, textureFactory);
            CreateAndAddChildComponent(c);
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

        card.Parent = Reference;
        card.PrototypeRef = PrototypeRef;

        card.Setup(PrototypeRef, index.ToString(), textureFactory);

        return card;
    }

    #endregion

    public override List<string> ValidateParameters(
        System.Collections.Generic.Dictionary<string, object> parameters
    )
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

    private int _spriteUpdateCountdown;

    private int _viewsInitialized = 0;

    private void RegisterInitializedViews()
    {
        _viewsInitialized++;

        if (_viewsInitialized == 2)
            UpdateDeckSprites();
    }

    private bool _frontTextureReady;
    private bool _backTextureReady;

    private void CheckForSpriteUpdate()
    {
        if (Children.Count > 0)
        {
            var c = ProjectService.Instance.GameObjects.GetComponent(Children.First());
            if (c is VcToken vcf)
            {
                if (vcf.TextureChanged && vcf.IsNodeReady() && vcf.BackTexture != null)
                {
                    _frontSprite.Texture = vcf.BackTexture;
                    _frontSprite.PixelSize = PixelSize(_frontSprite.Texture.GetSize());
                    _frontTextureReady = true;
                    vcf.TextureChanged = false;
                }
            }

            var l = ProjectService.Instance.GameObjects.GetComponent(Children.Last());
            if (l is VcToken vcb)
            {
                if (vcb.TextureChanged && vcb.IsNodeReady() && vcb.FaceTexture != null)
                {
                    _backSprite.Texture = vcb.FaceTexture;
                    _backSprite.PixelSize = PixelSize(_backSprite.Texture.GetSize());
                    _backTextureReady = true;
                    vcb.TextureChanged = false;
                }
            }

            TextureReady = _frontTextureReady && _backTextureReady;
        }
    }

    private void CreateAndAddChildComponent(VisualComponentBase component)
    {
        component.Location = ComponentLocation.Container;
        component.ExcludeFromSync = ExcludeFromSync;
        AddChildComponent(component);
        EventBus.Instance.Publish(new AddComponentToSceneEvent(component));
    }

    private float PixelSize(Vector2 size)
    {
        if (size.X == 0 || size.Y == 0)
            return 0;

        return 0.95f / Mathf.Max(size.X, size.Y);
    }

    private void UpdateDeckSprites()
    {
        //set the top and bottom sprites.
        if (_frontSprite == null || _backSprite == null)
            return;

        //The top of the deck displays the back of the first card.
        //The bottom of the deck displays the face of the last card.

        //TODO Handle if there are no cards in the deck?
        if (Children.Count > 0)
        {
            var c = ProjectService.Instance.GameObjects.GetComponent(Children.First());
            if (c is VcToken vcf)
            {
                if (vcf.TextureReady)
                {
                    if (vcf.BackTexture == null)
                        return;

                    _frontSprite.PixelSize = PixelSize(vcf.BackTexture.GetSize());
                    _frontSprite.Texture = vcf.BackTexture;

                    if (_mode == VcToken.TokenBuildMode.Grid && !_gridSingleBack)
                    {
                        int.TryParse(vcf.DataSetRow, out var frontFrame);
                        _frontSprite.Frame = frontFrame;
                        _frontSprite.Hframes = _gridCols;
                        _frontSprite.Vframes = _gridRows;
                        var ts = _frontSprite.Texture.GetSize();
                        var cv = new Vector2(ts.X / _gridCols, ts.Y / _gridRows);
                        _frontSprite.PixelSize = PixelSize(cv);
                    }

                    _frontTextureReady = true;
                }
            }

            var l = ProjectService.Instance.GameObjects.GetComponent(Children.Last());
            if (l is VcToken vcb)
            {
                if (vcb.TextureReady)
                {
                    if (vcb.FaceTexture == null)
                        return;

                    _backSprite.PixelSize = PixelSize(vcb.FaceTexture.GetSize());
                    _backSprite.Texture = vcb.FaceTexture;

                    if (_mode == VcToken.TokenBuildMode.Grid)
                    {
                        int.TryParse(vcb.DataSetRow, out var lastFrame);
                        _backSprite.Frame = lastFrame;
                        _backSprite.Hframes = _gridCols;
                        _backSprite.Vframes = _gridRows;
                        var ts = _backSprite.Texture.GetSize();
                        var cv = new Vector2(ts.X / _gridCols, ts.Y / _gridRows);
                        _backSprite.PixelSize = PixelSize(cv);
                    }

                    _backTextureReady = true;
                }
            }

            TextureReady = _frontTextureReady && _backTextureReady;

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

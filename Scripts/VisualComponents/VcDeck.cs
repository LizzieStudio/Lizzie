using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Godot;

public partial class VcDeck : VisualComponentGroup
{
    private Sprite3D _frontSprite;
    private Sprite3D _backSprite;
    private MeshInstance3D _sideMesh;

    private TokenTextureSubViewport _frontView;
    private TokenTextureSubViewport _backView;

    private Label3D _componentCount;

    private Node3D _spinnyBits;
    private Label3D _blankLabel;

    /// <summary>
    /// A deck's orientation is split across two nodes.
    /// X and Y rotate the whole thing, but Z only spins the deck model.
    /// </summary>
    public override Vector3 Rotation
    {
        get
        {
            var node = base.Rotation;
            return new Vector3(node.X, node.Y, _spinnyBits.Rotation.Z);
        }
        set
        {
            base.Rotation = new Vector3(value.X, value.Y, 0f);
            _spinnyBits.Rotation = new Vector3(0f, 0f, value.Z);
        }
    }

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Deck;

        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        _frontSprite = GetNode<Sprite3D>("%FrontSprite");
        _backSprite = GetNode<Sprite3D>("%BackSprite");
        _sideMesh = GetNode<MeshInstance3D>("%SideMesh");
        _componentCount = GetNode<Label3D>("ComponentCount");
        _spinnyBits = GetNode<Node3D>("SpinnyBits");
        DragDropCollider = GetNode<CollisionShape3D>("DrawCollider");
        _blankLabel = GetNode<Label3D>("%BlankLabel");
        UpdateComponentCount();

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

    public override Effect[] ProcessCommand(VisualCommand command)
    {
        if (command == VisualCommand.Flip)
            return new Effect[] { BuildFlip() };

        if (command == VisualCommand.Shuffle)
            return BuildShuffle();

        // this will work as long as the number commands remain in order
        if ((int)command >= (int)VisualCommand.Num1 && (int)command <= (int)VisualCommand.Num20)
            return BuildDraw((int)command + 1 - (int)VisualCommand.Num1);

        return base.ProcessCommand(command);
    }

    public override Effect[] ProcessCommandWithQuantity(VisualCommand command, int quantity)
    {
        // quantity == int.MaxValue means "All"; BuildDraw/BuildDeal clamp to deck size.
        if (command is VisualCommand.Draw)
            return BuildDraw(quantity);
        if (command is VisualCommand.Deal)
            return BuildDeal(quantity);

        return base.ProcessCommandWithQuantity(command, quantity);
    }

    private Effect[] BuildShuffle()
    {
        var seed = ((ulong)Rnd.Randi() << 32) | Rnd.Randi();
        return Shuffle(seed);
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
        l.Add(new MenuCommand(VisualCommand.Draw) { AddQtySubmenu = true, SingleOnly = true });
        l.Add(new MenuCommand(VisualCommand.Deal) { AddQtySubmenu = true, SingleOnly = true });
        return l;
    }

    private float _flipRate = 720; //degrees per second
    private bool _showFace = true;
    private int _rotMult = 1;
    private float _targetZ;
    private bool _flipInProcess;

    private TransformEffect BuildFlip()
    {
        var t = TransformEffect.Capture(this);
        t.Rotation = new Vector3(t.Rotation.X, t.Rotation.Y, _showFace ? Mathf.Pi : 0f);
        return t;
    }

    public override void AnimateFlip(Vector3 targetRotation)
    {
        _flipInProcess = true;
        _showFace = IsFaceUp(targetRotation);
        _rotMult = _showFace ? -1 : 1;
        _targetZ = _showFace ? 0 : 180;
    }

    private static bool IsFaceUp(Vector3 rotation) =>
        Mathf.Abs(Mathf.Wrap(rotation.Z, -Mathf.Pi, Mathf.Pi)) < Mathf.Pi / 2f;

    private void ProcessFlip(double delta)
    {
        var curZ = _spinnyBits.RotationDegrees.Z;
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

        _spinnyBits.RotationDegrees = new Vector3(0f, 0f, newZ);
    }

    /// <summary>
    /// The orientation a card takes as it is drawn off this deck.
    /// </summary>
    private Vector3 DrawnRotation(VisualComponentBase comp)
    {
        if (comp is not VcToken)
            return comp.Rotation;

        var z = Mathf.DegToRad(_showFace ? 180f : 0f);
        return new Vector3(comp.Rotation.X, comp.Rotation.Y, z);
    }

    private Effect[] BuildDraw(int count)
    {
        count = Math.Min(count, Children.Count);

        SnowportId[] cards;
        //draw cards
        if (_showFace)
        {
            cards = DrawFromTop(count);
        }
        else
        {
            cards = DrawFromBottom(count);
        }

        Effect[] result;

        //if there are player hands, draw to that. Otherwise draw to the table.
        if (ProjectService.Instance.CurrentProject.GameSettings.EnablePlayerHands)
        {
            int seat = PlayerHandService.LocalSeatIndex();
            var toHand = new List<Effect>(cards.Length);

            for (int i = 0; i < cards.Length; i++)
            {
                var comp = ProjectService.Instance.GameObjects.GetComponent(cards[i]);
                if (comp == null)
                    continue;
                toHand.Add(PlayerHandService.Instance.MoveEffect(comp, seat, i));
            }

            result = toHand.ToArray();
        }
        else
        {
            //splay onto the board via a Transform event
            var transformed = new List<Effect>();

            for (int i = 0; i < cards.Length; i++)
            {
                var comp = ProjectService.Instance.GameObjects.GetComponent(cards[i]);

                if (comp == null)
                    continue;

                float deltaX = Position.X + (_width * (1.5f + i));

                var t = TransformEffect.Capture(comp);
                t.Location = ComponentLocation.Table;
                t.ContainerRef = SnowportId.Empty;
                t.Position = new Vector3(deltaX, Position.Y, Position.Z);
                t.Rotation = DrawnRotation(comp);
                // Splayed cards land on top, in draw order.
                t.ZTarget = ZTarget.Top;
                t.ZSuborder = i;
                transformed.Add(t);
            }

            result = transformed.ToArray();
        }

        return result;
    }

    /// <summary>
    /// Deals <paramref name="countPerPlayer"/> cards to every active player seat in turn,
    /// like a real deal (seat 0 gets a card, seat 1 gets a card, …, repeat).
    /// If the deck runs out before all rounds are complete the remaining seats get fewer cards.
    /// </summary>
    private Effect[] BuildDeal(int countPerPlayer)
    {
        var settings = ProjectService.Instance.CurrentProject?.GameSettings;
        if (settings == null || settings.Players.Count == 0)
            return BuildDraw(countPerPlayer); // fall back to draw if no seats defined

        int seatCount = settings.Players.Count;

        var order = Children.ToList();
        if (!_showFace)
            order.Reverse(); // deal from the bottom when the deck is face-down

        int total = Math.Min(countPerPlayer * seatCount, order.Count);

        var handService = PlayerHandService.Instance;
        var containers = new SnowportId[seatCount];
        for (int seat = 0; seat < seatCount; seat++)
            containers[seat] = handService.HandContainer(seat);

        // Deal round-robin.
        var effects = new List<Effect>(total);
        var suborder = new int[seatCount];
        for (int i = 0; i < total; i++)
        {
            int seat = i % seatCount;
            if (containers[seat] == SnowportId.Empty)
                continue;
            var comp = ProjectService.Instance.GameObjects.GetComponent(order[i]);
            if (comp == null)
                continue;
            effects.Add(handService.MoveEffect(comp, seat, suborder[seat]++));
        }

        return effects.ToArray();
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
        BuildInternal((PrintedParameters)proto.Parameters, textureFactory);
    }

    public override IEnumerable<CreateEffect> GetSpawnChildEffects(SnowportId containerRef)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            yield break;
        if (!project.Prototypes.TryGetValue(PrototypeRef, out var proto))
            yield break;
        if (proto.Parameters is not PrintedParameters parameters)
            yield break;

        int index = 0;
        foreach (var row in EnumerateCardRows(parameters, project))
        {
            yield return CreateCardEffect(row, containerRef, index);
            index++;
        }
    }

    private IEnumerable<string> EnumerateCardRows(PrintedParameters parameters, Project project)
    {
        switch (parameters.Mode)
        {
            case VcToken.TokenBuildMode.QuickDeck:
            {
                int cardNum = 1;
                foreach (var q in parameters.QuickCardData ?? new())
                {
                    foreach (var _ in Utility.ParseValueRanges(q.Caption))
                    {
                        yield return cardNum.ToString();
                        cardNum++;
                    }
                }
                break;
            }

            case VcToken.TokenBuildMode.Template:
            {
                var dataset = project.Datasets[parameters.Dataset];
                foreach (var kv in dataset.Rows)
                    yield return kv.Key;
                break;
            }

            case VcToken.TokenBuildMode.Grid:
            {
                for (int i = 0; i < parameters.GridCount; i++)
                    yield return i.ToString();
                break;
            }
        }
    }

    private CreateEffect CreateCardEffect(string dataSetRow, SnowportId containerRef, int index)
    {
        var id = Snowport.Clock.Create();
        return new CreateEffect
        {
            ComponentRef = id,
            PrototypeRef = PrototypeRef,
            ComponentName = ComponentName,
            State = new VcSyncDto
            {
                DataSetRow = dataSetRow,
                Location = ComponentLocation.Container,
                ContainerRef = containerRef,
                // First enumerated card are at the top.
                ZOrder = new ZOrder(ZTarget.Top, -index, SnowportId.Empty),
            },
        };
    }

    public override bool Setup(
        ComponentParameters parameters,
        string dataSetRow,
        TextureFactory textureFactory
    )
    {
        return BuildInternal((PrintedParameters)parameters, textureFactory);
    }

    private bool BuildInternal(PrintedParameters parameters, TextureFactory textureFactory)
    {
        base.Setup(parameters, DataSetRow, textureFactory);

        _frontSprite = GetNode<Sprite3D>("%FrontSprite");
        _backSprite = GetNode<Sprite3D>("%BackSprite");

        _frontView = GetNode<TokenTextureSubViewport>("FrontViewport");
        _backView = GetNode<TokenTextureSubViewport>("BackViewport");

        _blankLabel = GetNode<Label3D>("%BlankLabel");

        if (!InitializeParameters(parameters))
            return false;

        _blankLabel.Text = ComponentName;

        UpdateThickness();

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

        _showFace = IsFaceUp(Rotation);

        UpdateDeckSprites();
        return true;
    }

    public override bool Refresh(TextureFactory textureFactory)
    {
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

    private bool InitializeParameters(PrintedParameters parameters)
    {
        if (parameters.Height <= 0)
            return false;
        _height = parameters.Height / 10f;

        _width = parameters.Width / 10f;

        return true;
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
                    ApplyTokenToSprite(
                        _frontSprite,
                        vcf.BackTexture,
                        vcf.BackFrame,
                        vcf.BackHframes,
                        vcf.BackVframes
                    );
                    _frontTextureReady = true;
                    vcf.TextureChanged = false;
                }
            }

            var l = ProjectService.Instance.GameObjects.GetComponent(Children.Last());
            if (l is VcToken vcb)
            {
                if (vcb.TextureChanged && vcb.IsNodeReady() && vcb.FaceTexture != null)
                {
                    ApplyTokenToSprite(
                        _backSprite,
                        vcb.FaceTexture,
                        vcb.FaceFrame,
                        vcb.FaceHframes,
                        vcb.FaceVframes
                    );
                    _backTextureReady = true;
                    vcb.TextureChanged = false;
                }
            }

            TextureReady = _frontTextureReady && _backTextureReady;
        }
    }

    private void ApplyTokenToSprite(
        Sprite3D sprite,
        Texture2D tex,
        int frame,
        int hframes,
        int vframes
    )
    {
        int cols = Math.Max(hframes, 1);
        int rows = Math.Max(vframes, 1);
        sprite.Texture = tex;
        sprite.Hframes = cols;
        sprite.Vframes = rows;
        sprite.Frame = Math.Max(frame, 0);
        var ts = tex.GetSize();
        var cellSize = new Vector2(ts.X / cols, ts.Y / rows);
        sprite.PixelSize = PixelSize(cellSize);
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
        if (_frontSprite == null || _backSprite == null || _sideMesh == null)
            return;

        //The top of the deck displays the back of the first card.
        //The bottom of the deck displays the face of the last card.

        if (Children.Count > 0)
        {
            var c = ProjectService.Instance.GameObjects.GetComponent(Children.First());
            if (c is VcToken vcf)
            {
                if (vcf.TextureReady)
                {
                    if (vcf.BackTexture == null)
                        return;

                    ApplyTokenToSprite(
                        _frontSprite,
                        vcf.BackTexture,
                        vcf.BackFrame,
                        vcf.BackHframes,
                        vcf.BackVframes
                    );

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

                    ApplyTokenToSprite(
                        _backSprite,
                        vcb.FaceTexture,
                        vcb.FaceFrame,
                        vcb.FaceHframes,
                        vcb.FaceVframes
                    );

                    _backTextureReady = true;
                }
            }

            _frontSprite.Visible = true;
            _backSprite.Visible = true;
            _sideMesh.Visible = true;

            TextureReady = _frontTextureReady && _backTextureReady;
        }
        else
        {
            _frontSprite.Visible = false;
            _backSprite.Visible = false;
            _sideMesh.Visible = false;
        }
    }

    private float _height;
    private float _width;
    private float _thickness;
    private string _frontImage;
    private string _backImage;
    private int _shape;
    private Color _frontBgColor;
    private string _frontCaption;
    private Color _frontCaptionColor;
    private bool _differentBack;
    private Color _backBgColor;
    private string _backCaption;
    private Color _backCaptionColor;

    protected override void OnChildrenChanged()
    {
        UpdateDeckSprites();
        UpdateThickness();
        UpdateComponentCount();
    }

    private void UpdateComponentCount()
    {
        if (_componentCount == null)
            return;
        _componentCount.Text = Children.Count().ToString();
    }

    private const float MinimumThickness = 0.15f;

    private void UpdateThickness()
    {
        // The empty deck thickness fixes a bug where you couldn't interact with an empty deck
        _thickness = Math.Max(MinimumThickness, 0.03f * Children.Count);
        YHeight = _thickness + 0.03f;

        Scale = new Vector3(_width, _thickness, _height);
        EventBus.Instance.Publish(new QueueStackingUpdateEvent());
    }

    public override TableEvent DragDraw(int count)
    {
        count = Math.Min(count, Children.Count);

        var cards = _showFace ? DrawFromTop(count) : DrawFromBottom(count);

        return ProjectService.Instance.GameObjects.BuildDrawEvent(cards, DrawnRotation);
    }

    #region Drop Processing


    public override bool CanObjectsBeDropped(IEnumerable<VisualComponentBase> dragObjects)
    {
        float epsilon = 0.01f;

        foreach (var c in dragObjects)
        {
            if (c is not VcToken token)
                return false;

            //check size
            if (
                Math.Abs(token.Height - _height) > epsilon
                || Math.Abs(token.Width - _width) > epsilon
            )
                return false;
        }

        return true;
    }

    public override TableEvent DropObjects(IEnumerable<VisualComponentBase> dragObjects) =>
        AddChildComponents(dragObjects, _showFace);

    #endregion
}

public sealed class DeckParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Deck;
}

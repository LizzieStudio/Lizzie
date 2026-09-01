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

    public override CommandResponse ProcessCommandWithQuantity(VisualCommand command, int quantity)
    {
        // quantity == int.MaxValue means "All"; DrawCards/DealCards clamp to deck size.
        return command switch
        {
            VisualCommand.Draw => DrawCards(quantity),
            VisualCommand.Deal => DealCards(quantity),
            _ => base.ProcessCommandWithQuantity(command, quantity),
        };
    }

    private CommandResponse PerformShuffle()
    {
        EventSynchronizer.Instance?.Submit(
            new ComponentShuffledEvent
            {
                Id = Snowport.Clock.Create(),
                ComponentRef = Reference,
                Seed = Rnd.Randi(),
            }
        );

        return new CommandResponse(true, null);
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

    private CommandResponse StartFlip()
    {
        EventSynchronizer.Instance?.Submit(
            new ComponentFlippedEvent
            {
                Id = Snowport.Clock.Create(),
                ComponentRef = Reference,
                FaceUp = !_showFace,
            }
        );

        return new CommandResponse(true, null);
    }

    public override void AnimateFlip(bool faceUp)
    {
        _flipInProcess = true;
        _showFace = faceUp;
        _rotMult = _showFace ? -1 : 1;
        _targetZ = _showFace ? 0 : 180;
    }

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

        _spinnyBits.RotationDegrees = new Vector3(RotationDegrees.X, RotationDegrees.Y, newZ);
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

    private CommandResponse DrawCards(int count)
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
            cards = cards.Reverse().ToArray();
        }

        var cl = new List<VcToken>();

        for (int i = 0; i < cards.Length; i++)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(cards[i]);
            if (comp is VcToken token)
                cl.Add(token);
        }

        //if there are player hands, draw to that. Otherwise draw to the table.
        if (ProjectService.Instance.CurrentProject.GameSettings.EnablePlayerHands)
        {
            // SeatIndex -2 resolves to the local player's seat inside PlayerHandService.
            EventBus.Instance.Publish(new AddToHandEvent { Cards = cl, SeatIndex = -2 });
        }
        else
        {
            //splay onto the board via a Transform event
            var transformed = new List<TransformedComponent>();

            for (int i = 0; i < cards.Length; i++)
            {
                var comp = ProjectService.Instance.GameObjects.GetComponent(cards[i]);

                if (comp == null)
                    continue;

                float deltaX = Position.X + (_width * (1.5f + i));

                var t = TransformedComponent.Capture(comp);
                t.Location = ComponentLocation.Board;
                t.Position = new Vector3(deltaX, Position.Y, Position.Z);
                t.Rotation = DrawnRotation(comp);
                // Splayed cards land on top, in draw order.
                t.ZTarget = ZTarget.Top;
                t.ZSuborder = i;
                transformed.Add(t);
            }

            if (transformed.Count > 0)
            {
                EventSynchronizer.Instance?.Submit(
                    new ComponentsTransformedEvent
                    {
                        Id = Snowport.Clock.Create(),
                        Components = transformed.ToArray(),
                    }
                );
            }
        }

        var change = new Change
        {
            Action = Change.ChangeType.Transform,
            Begin = Transform,
            End = Transform,
            Component = this,
        };

        UpdateDeckSprites();

        return new CommandResponse(true, change);
    }

    /// <summary>
    /// Deals <paramref name="countPerPlayer"/> cards to every active player seat in turn,
    /// like a real deal (seat 0 gets a card, seat 1 gets a card, …, repeat).
    /// If the deck runs out before all rounds are complete the remaining seats get fewer cards.
    /// </summary>
    private CommandResponse DealCards(int countPerPlayer)
    {
        var settings = ProjectService.Instance.CurrentProject?.GameSettings;
        if (settings == null || settings.Players.Count == 0)
            return DrawCards(countPerPlayer); // fall back to draw if no seats defined

        int seatCount = settings.Players.Count;

        // Collect all cards in dealing order: round-robin across seats
        var handsToAdd = new Dictionary<int, List<VcToken>>();
        for (int seat = 0; seat < seatCount; seat++)
            handsToAdd[seat] = new List<VcToken>();

        for (int round = 0; round < countPerPlayer; round++)
        {
            for (int seat = 0; seat < seatCount; seat++)
            {
                if (Children.Count == 0)
                    break;

                SnowportId[] drawn = _showFace ? DrawFromTop(1) : DrawFromBottom(1);
                if (drawn.Length == 0)
                    break;

                var comp = ProjectService.Instance.GameObjects.GetComponent(drawn[0]);
                if (comp is VcToken token)
                    handsToAdd[seat].Add(token);
            }
        }

        // Publish one AddToHandEvent per seat
        foreach (var kv in handsToAdd)
        {
            if (kv.Value.Count > 0)
                EventBus.Instance.Publish(
                    new AddToHandEvent { Cards = kv.Value, SeatIndex = kv.Key }
                );
        }

        UpdateDeckSprites();

        var change = new Change
        {
            Action = Change.ChangeType.Transform,
            Begin = Transform,
            End = Transform,
            Component = this,
        };
        return new CommandResponse(true, change);
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

    public override void SpawnChildEvents()
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return;
        if (!project.Prototypes.TryGetValue(PrototypeRef, out var proto))
            return;
        if (proto.Parameters is not PrintedParameters parameters)
            return;

        Children.Clear();

        foreach (var row in EnumerateCardRows(parameters, project))
            SubmitCardCreation(row);

        OnChildrenChanged();
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

    private void SubmitCardCreation(string dataSetRow)
    {
        var id = Snowport.Clock.Create();
        EventSynchronizer.Instance?.Submit(
            new ComponentCreatedEvent
            {
                Id = id,
                PrototypeRef = PrototypeRef,
                ComponentName = ComponentName,
                State = new VcSyncDto
                {
                    DataSetRow = dataSetRow,
                    Location = ComponentLocation.Container,
                    LogicalVisible = false,
                },
            }
        );
        Children.Add(id);
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

        UpdateDeckSprites();
        SyncRequired = true;
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

    public override void DragDraw(int count)
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
            cards = cards.Reverse().ToArray();
        }

        for (int i = 0; i < cards.Length; i++)
        {
            var comp = ProjectService.Instance.GameObjects.GetComponent(cards[i]);

            if (comp == null)
                continue;

            comp.Rotation = DrawnRotation(comp);
        }

        UpdateDeckSprites();

        if (!cards.Any())
            return;

        ProjectService.Instance.GameObjects.ShowAndDrag(cards.ToList());
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

    public override void DropObjects(IEnumerable<VisualComponentBase> dragObjects)
    {
        // Add to the top or bottom of deck depending on orientation

        if (_showFace)
        {
            AddChildComponents(dragObjects, true);
        }
        else
        {
            AddChildComponents(dragObjects, false);
        }
    }

    #endregion
}

public sealed class DeckParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Deck;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

public partial class VcDeck : VisualComponentBase
{
    private Sprite3D _outline;
    private Label3D _componentCount;
    private Label3D _blankLabel;

    private readonly RandomNumberGenerator _rnd = new();

    public override void _Ready()
    {
        base._Ready();
        ComponentType = VisualComponentType.Deck;

        HighlightMesh = GetNode<MeshInstance3D>("HighlightMesh");
        _outline = GetNode<Sprite3D>("Outline");
        _componentCount = GetNode<Label3D>("ComponentCount");
        _blankLabel = GetNode<Label3D>("%BlankLabel");
    }

    public override GeometryInstance3D DragMesh => _outline;

    public override float MaxAxisSize => Math.Max(_height, _width);

    /// <summary>The cards on this deck, top first.</summary>
    private List<VisualComponentBase> Stack() => ProjectService.Instance.GameObjects.GetStack(this);

    /// <summary>
    /// For showing how many cards are on this deck.
    /// </summary>
    public void SetCount(int count)
    {
        _componentCount.Text = count.ToString();
    }

    public override Effect[] ProcessCommand(VisualCommand command)
    {
        if (command == VisualCommand.Flip)
            return BuildFlip();

        if (command == VisualCommand.Shuffle)
            return BuildShuffle();

        if (command == VisualCommand.RotateCcw)
            return BuildRotation(ProjectService.Instance.RotationStep);

        if (command == VisualCommand.RotateCw)
            return BuildRotation(-1 * ProjectService.Instance.RotationStep);

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

    public override List<MenuCommand> GetMenuCommands()
    {
        var l = base.GetMenuCommands();
        l.Add(new MenuCommand(VisualCommand.Flip));
        l.Add(new MenuCommand(VisualCommand.Shuffle));
        l.Add(new MenuCommand(VisualCommand.Draw) { AddQtySubmenu = true, SingleOnly = true });
        l.Add(new MenuCommand(VisualCommand.Deal) { AddQtySubmenu = true, SingleOnly = true });
        return l;
    }

    /// <summary>
    /// Turns each card over and reverses their order by swapping their existing ZOrders.
    /// </summary>
    private Effect[] BuildFlip()
    {
        var cards = Stack();
        var effects = new List<Effect>(cards.Count);
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] is VcToken card)
            {
                var z = cards[cards.Count - 1 - i].ZOrder;
                effects.Add(new ComponentEffect(card.BuildFlipState() with { ZOrder = z }));
            }
        }

        return effects.ToArray();
    }

    /// <summary>
    /// Permutes the cards' existing ZOrders using Fisher-Yates.
    /// </summary>
    private Effect[] BuildShuffle()
    {
        var cards = Stack();
        var orders = cards.Select(c => c.ZOrder).ToList();

        for (int n = orders.Count - 1; n > 0; n--)
        {
            var r = _rnd.RandiRange(0, n);
            (orders[r], orders[n]) = (orders[n], orders[r]);
        }

        return cards
            .Select(
                (c, i) =>
                    (Effect)
                        new ComponentEffect(ComponentState.Capture(c) with { ZOrder = orders[i] })
            )
            .ToArray();
    }

    /// <summary>
    /// Rotates the frame and its cards together.
    /// </summary>
    private Effect[] BuildRotation(float degreesAboutY)
    {
        var step = new Vector3(0, Mathf.DegToRad(degreesAboutY), 0);
        return Stack()
            .Append(this)
            .Select(c =>
                (Effect)
                    new ComponentEffect(
                        ComponentState.Capture(c) with
                        {
                            Rotation = c.Rotation + step,
                        }
                    )
            )
            .ToArray();
    }

    private Effect[] BuildDraw(int count)
    {
        var cards = Stack().Take(count).ToList();
        var stamp = Snowport.Clock.Create();

        //if there are player hands, draw to that. Otherwise draw to the table.
        if (ProjectService.Instance.Settings.Value.EnablePlayerHands)
        {
            int seat = PlayerHandService.LocalSeatIndex();
            return cards
                .Select((c, i) => (Effect)PlayerHandService.Instance.MoveEffect(c, seat, i, stamp))
                .ToArray();
        }

        //splay onto the board
        return cards
            .Select(
                (c, i) =>
                    (Effect)
                        new ComponentEffect(
                            ComponentState.Capture(c) with
                            {
                                Position = new Vector3(
                                    Position.X + (_width * (1.5f + i)),
                                    Position.Y,
                                    Position.Z
                                ),
                                // Splayed cards land on top, in draw order.
                                ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                            }
                        )
            )
            .ToArray();
    }

    /// <summary>
    /// Deals <paramref name="countPerPlayer"/> cards to every active player seat in turn,
    /// like a real deal (seat 0 gets a card, seat 1 gets a card, …, repeat).
    /// If the deck runs out before all rounds are complete the remaining seats get fewer cards.
    /// </summary>
    private Effect[] BuildDeal(int countPerPlayer)
    {
        var settings = ProjectService.Instance.Settings.Value;
        if (settings == null || settings.Players.Length == 0)
            return BuildDraw(countPerPlayer); // fall back to draw if no seats defined

        int seatCount = settings.Players.Length;
        var order = Stack();
        var handService = PlayerHandService.Instance;

        var activeSeats = new List<int>(seatCount);
        for (int seat = 0; seat < seatCount; seat++)
        {
            if (handService.HandContainer(seat) != SnowTag.Empty)
                activeSeats.Add(seat);
        }

        if (activeSeats.Count == 0)
            return [];

        int total = (int)Math.Min((long)countPerPlayer * activeSeats.Count, order.Count);

        // Deal round-robin.
        var effects = new List<Effect>(total);
        var suborder = new int[seatCount];
        var stamp = Snowport.Clock.Create();
        for (int i = 0; i < total; i++)
        {
            int seat = activeSeats[i % activeSeats.Count];
            effects.Add(handService.MoveEffect(order[i], seat, suborder[seat]++, stamp));
        }

        return effects.ToArray();
    }

    public override IEnumerable<ComponentEffect> GetDespawnEffects() =>
        Stack()
            .Append(this)
            .Select(c => new ComponentEffect(ComponentState.Capture(c) with { Deleted = true }));

    /// <summary>
    /// The deck's cards, face-down on the frame, with the first token on top.
    /// </summary>
    public override IEnumerable<ComponentState> GetSpawnStack(ComponentState self)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return [];
        var proto = ProjectService.Instance.GetIncludingDeleted<Prototype>(PrototypeRef);
        if (proto?.Parameters is not PrintedParameters parameters)
            return [];

        return EnumerateCardRows(parameters)
            .Reverse()
            .Select(row => new ComponentState
            {
                Id = Snowport.Clock.CreateTag(),
                PrototypeRef = PrototypeRef,
                DataSetRowIndex = row.Index,
                DataSetRowId = row.Id,
                Location = ComponentLocation.Table,
                X = self.X,
                Z = self.Z,
                Rotation = new Vector3(0, self.Rotation.Y, Mathf.Pi),
            })
            .ToList();
    }

    private static IEnumerable<(int Index, SnowTag Id)> EnumerateCardRows(
        PrintedParameters parameters
    )
    {
        switch (parameters.Mode)
        {
            case VcToken.TokenBuildMode.QuickDeck:
            {
                int cardNum = 0;
                foreach (var q in parameters.QuickCardData)
                {
                    foreach (var _ in Utility.ParseValueRanges(q.Caption))
                    {
                        yield return (cardNum, SnowTag.Empty);
                        cardNum++;
                    }
                }
                break;
            }

            case VcToken.TokenBuildMode.Template:
            {
                foreach (var row in ProjectService.Instance.GetRows(parameters.Dataset))
                    yield return (-1, row.Id);
                break;
            }

            case VcToken.TokenBuildMode.Grid:
            {
                for (int i = 0; i < parameters.GridCount; i++)
                    yield return (i, SnowTag.Empty);
                break;
            }
        }
    }

    protected override void Setup(ComponentParameters parameters, IRecordReader R)
    {
        base.Setup(parameters, R);

        _blankLabel = GetNode<Label3D>("%BlankLabel");

        var p = (PrintedParameters)parameters;
        if (p.Height <= 0)
            return;

        _height = p.Height / 10f;
        _width = p.Width / 10f;
        _blankLabel.Text = ComponentName;

        YHeight = Thickness + 0.03f;
        Scale = new Vector3(_width, Thickness, _height);
        EventBus.Instance.Publish(new QueueStackingUpdateEvent());

        ShapeProfiles.Add(
            VcToken.ShapeProfile((TokenTextureSubViewport.TokenShape)p.Shape, _width, _height)
        );

        TextureReady = true;
    }

    private float _height;
    private float _width;

    private const float Thickness = 0.15f;
}

public sealed record DeckParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Deck;
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static VisualComponentBase;

/// <summary>
/// Commands for decks.
/// A deck is a frame, and its cards are the tokens stacked exactly on it.
/// </summary>
public static class DeckCommands
{
    public static readonly Command Shuffle = new RecordCommand<ComponentState>
    {
        Name = new("deck.shuffle"),
        Icon = CommandIcons.Shuffle,
        Caption = "Shuffle {0}",
        Noun = ("Deck", "Decks"),
        AppliesTo = HasCards,
        Effects = (R, cs, _) => cs.SelectMany(c => Shuffled(R, c)),
    };

    public static readonly Command Draw = new RecordCommand<ComponentState>
    {
        Name = new("deck.draw"),
        Icon = CommandIcons.Draw,
        Caption = "Draw Cards",
        AsksForNumber = true,
        InfiniteOption = "All",
        NumberKeys = true,
        // As many cards as the fullest deck has.
        NumberLimit = (R, decks) => decks.Select(d => R.TokensOn(d).Count).DefaultIfEmpty().Max(),
        AppliesTo = HasCards,
        Effects = (R, cs, n) => cs.SelectMany(c => Drawn(R, c, n)),
    };

    public static readonly Command Deal = new RecordCommand<ComponentState>
    {
        Name = new("deck.deal"),
        Icon = CommandIcons.Deal,
        Caption = "Deal Cards",
        AsksForNumber = true,
        InfiniteOption = "All",
        // As many cards as each seat can get from the fullest deck.
        NumberLimit = (R, decks) =>
            decks.Select(d => PerSeat(R.TokensOn(d).Count, DealSeats(R))).DefaultIfEmpty().Max(),
        AppliesTo = HasCards,
        Effects = (R, cs, n) => cs.SelectMany(c => Dealt(R, c, n)),
    };

    private static bool IsDeck(IRecordReader R, ComponentState c) =>
        R.Kind(c) == VisualComponentType.Deck;

    // There's nothing to shuffle, draw or deal in an empty deck.
    private static bool HasCards(IRecordReader R, ComponentState c) =>
        IsDeck(R, c) && R.TokensOn(c).Count > 0;

    /// <summary>The deck's cards with their ZOrders shuffled among them.</summary>
    private static IEnumerable<ComponentState> Shuffled(IRecordReader R, ComponentState deck)
    {
        var cards = R.TokensOn(deck);
        var orders = cards.Select(c => c.ZOrder).ToArray();
        Random.Shared.Shuffle(orders);
        return cards.Select((c, i) => c with { ZOrder = orders[i] });
    }

    /// <summary>
    /// The top <paramref name="count"/> cards drawn into the local player's hand when hands are on,
    /// otherwise splayed beside the deck. <see cref="int.MaxValue"/> draws them all.
    /// </summary>
    private static IEnumerable<ComponentState> Drawn(
        IRecordReader R,
        ComponentState deck,
        int count
    )
    {
        var cards = R.TokensOn(deck).Take(count).ToList();
        var stamp = Snowport.Clock.Create();

        if (R.Single<ProjectGameSettings>().EnablePlayerHands)
        {
            var hands = PlayerHandService.Instance;
            if (hands == null)
                return [];
            int seat = PlayerHandService.LocalSeatIndex();
            return cards.Select((c, i) => hands.MovedToHand(c, seat, i, stamp));
        }

        // Splayed cards land to the right of the deck and on top, in draw order.
        float width = R.GetIncludingDeleted<Prototype>(deck.PrototypeRef)?.Parameters
            is PrintedParameters p
            ? p.Width / 10f
            : 0;
        var frame = deck.PositionAt(0);
        return cards.Select(
            (c, i) =>
                c with
                {
                    Position = frame + new Vector3(width * (1.5f + i), 0, 0),
                    ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                }
        );
    }

    /// <summary>
    /// Deals <paramref name="countPerPlayer"/> cards to every seat with a hand in turn, like a real deal.
    /// When the deck runs out, the last seats get fewer. With no seats, the local player draws instead.
    /// </summary>
    private static IEnumerable<ComponentState> Dealt(
        IRecordReader R,
        ComponentState deck,
        int countPerPlayer
    )
    {
        if (DealSeats(R) is not { } seats)
            return Drawn(R, deck, countPerPlayer);
        if (seats.Count == 0)
            return [];

        var hands = PlayerHandService.Instance;
        var cards = R.TokensOn(deck);
        int total = (int)Math.Min((long)countPerPlayer * seats.Count, cards.Count);
        var suborder = new int[seats.Max() + 1];
        var stamp = Snowport.Clock.Create();
        var dealt = new List<ComponentState>(total);
        for (int i = 0; i < total; i++)
        {
            int seat = seats[i % seats.Count];
            dealt.Add(hands.MovedToHand(cards[i], seat, suborder[seat]++, stamp));
        }
        return dealt;
    }

    /// <summary>
    /// The seats a deal goes to: every seat with a hand. Null when the project has no seats,
    /// so the local player draws instead.
    /// </summary>
    private static List<int> DealSeats(IRecordReader R)
    {
        var players = R.Single<ProjectGameSettings>().Players.Length;
        if (players == 0)
            return null;

        var hands = PlayerHandService.Instance;
        if (hands == null)
            return [];
        return Enumerable
            .Range(0, players)
            .Where(seat => hands.HandContainer(seat) != SnowTag.Empty)
            .ToList();
    }

    /// <summary>How many of <paramref name="cards"/> each seat can be dealt, rounding up.</summary>
    private static int PerSeat(int cards, List<int> seats) =>
        seats switch
        {
            null => cards,
            [] => 0,
            _ => (cards + seats.Count - 1) / seats.Count,
        };
}

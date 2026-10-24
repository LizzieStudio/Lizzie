using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static Lizzie.VisualComponentBase;

namespace Lizzie;

/// <summary>
/// Commands for decks.
/// </summary>
public static class DeckCommands
{
    [Command("deck.shuffle")]
    public static readonly Command Shuffle = new AtomCommand<ComponentPosition>
    {
        Icon = CommandIcons.Shuffle,
        Caption = "Shuffle {0}",
        Noun = ("Deck", "Decks"),
        AppliesTo = HasCards,
        Effects = (R, cs, _) => cs.SelectMany(c => Shuffled(R, c)),
    };

    [Command("deck.draw")]
    public static readonly Command Draw = new AtomCommand<ComponentPosition>
    {
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

    [Command("deck.deal")]
    public static readonly Command Deal = new AtomCommand<ComponentPosition>
    {
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

    private static bool IsDeck(IAtomReader R, ComponentPosition c) =>
        R.Kind(c) == VisualComponentType.Deck;

    // There's nothing to shuffle, draw or deal in an empty deck.
    private static bool HasCards(IAtomReader R, ComponentPosition c) =>
        IsDeck(R, c) && R.TokensOn(c).Count > 0;

    /// <summary>The deck's cards with their ZOrders shuffled among them.</summary>
    private static IEnumerable<ComponentPosition> Shuffled(IAtomReader R, ComponentPosition deck)
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
    private static IEnumerable<ComponentPosition> Drawn(
        IAtomReader R,
        ComponentPosition deck,
        int count
    )
    {
        var cards = R.TokensOn(deck).Take(count).ToList();
        var stamp = SnowportId.Create();

        if (R.Get(ProjectGameSettings.Only).EnablePlayerHands)
        {
            // A player without a seat has no hand to draw into.
            int seat = R.LocalSeat();
            if (seat < 0)
                return [];
            return cards.Select((c, i) => PlayerHandService.MovedToHand(c, seat, i, stamp));
        }

        // Splayed cards land to the right of the deck and on top, in draw order.
        float width = R.GetPrototype(deck)?.Parameters is PrintedParameters p ? p.Width / 10f : 0;
        var frame = deck.PositionAt(0);
        return cards.Select(
            (c, i) =>
                c with
                {
                    ContainerRef = SnowTag.Empty,
                    Position = frame + new Vector3(width * (1.5f + i), 0, 0),
                    Rotation = c.Rotation with { Y = deck.Rotation.Y + c.Rotation.Y },
                    ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                }
        );
    }

    /// <summary>
    /// Deals <paramref name="countPerPlayer"/> cards to every seat with a hand in turn, like a real deal.
    /// When the deck runs out, the last seats get fewer. With no seats, the local player draws instead.
    /// </summary>
    private static IEnumerable<ComponentPosition> Dealt(
        IAtomReader R,
        ComponentPosition deck,
        int countPerPlayer
    )
    {
        if (DealSeats(R) is not { } seats)
            return Drawn(R, deck, countPerPlayer);
        if (seats.Count == 0)
            return [];

        var cards = R.TokensOn(deck);
        int total = (int)Math.Min((long)countPerPlayer * seats.Count, cards.Count);
        var suborder = new int[seats.Max() + 1];
        var stamp = SnowportId.Create();
        var dealt = new List<ComponentPosition>(total);
        for (int i = 0; i < total; i++)
        {
            int seat = seats[i % seats.Count];
            dealt.Add(PlayerHandService.MovedToHand(cards[i], seat, suborder[seat]++, stamp));
        }
        return dealt;
    }

    /// <summary>
    /// The seats a deal goes to: every seat with a hand. Null when the project has no seats,
    /// so the local player draws instead.
    /// </summary>
    private static List<int> DealSeats(IAtomReader R)
    {
        var players = R.Get(ProjectGameSettings.Only).Players.Length;
        if (players == 0)
            return null;

        return Enumerable.Range(0, players).Where(seat => R.HandOf(seat) != SnowTag.Empty).ToList();
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

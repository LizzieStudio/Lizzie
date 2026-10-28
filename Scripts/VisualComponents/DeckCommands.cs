using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.Replication.Machinery;
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
        Effects = (R, cs) => cs.SelectMany(c => Shuffled(R, c)),
    };

    [Command("deck.draw")]
    public static readonly Command Draw = new ChoiceCommand<ComponentPosition>
    {
        Icon = CommandIcons.Draw,
        Caption = "Draw Cards",
        NumberKeys = true,
        AppliesTo = HasCards,
        // As many cards as the fullest deck has.
        Choices = (R, decks) => Counts(decks.Max(d => CardCount(R, d))),
        Effects = (R, cs, n) => cs.SelectMany(c => Drawn(R, c, n)),
    };

    [Command("deck.deal")]
    public static readonly Command Deal = new ChoiceCommand<ComponentPosition>
    {
        Icon = CommandIcons.Deal,
        Caption = "Deal Cards",
        AppliesTo = HasCards,
        // As many cards as each seat can get from the fullest deck.
        Choices = (R, decks) =>
            Counts(decks.Max(d => PerSeat(CardCount(R, d), R.Count(Seat.Live)))),
        Effects = (R, cs, n) => cs.SelectMany(c => Dealt(R, c, n)),
    };

    /// <summary>
    /// The counts from 1 to <paramref name="max"/>, no more than there are number keys, then "All".
    /// </summary>
    private static IReadOnlyList<(string Label, int Value)> Counts(int max) =>
        [
            .. Enumerable
                .Range(1, Math.Min(max, ShortcutActions.NumberCount))
                .Select(n => (n.ToString(), n)),
            ("All", int.MaxValue),
        ];

    private static bool IsDeck(IAtomReader R, ComponentPosition c) =>
        R.Kind(c) == VisualComponentType.Deck;

    // There's nothing to shuffle, draw or deal in an empty deck.
    private static bool HasCards(IAtomReader R, ComponentPosition c) =>
        IsDeck(R, c) && CardCount(R, c) > 0;

    private static int CardCount(IAtomReader R, ComponentPosition deck) =>
        R.Count(ComponentPosition.ByContainer, deck.Inside);

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
            var seat = R.LocalSeat();
            if (seat.IsEmpty)
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
                    Container = null,
                    Position = frame + new Vector3(width * (1.5f + i), 0, 0),
                    Rotation = c.Rotation with { Y = deck.Rotation.Y + c.Rotation.Y },
                    ZOrder = new ZOrder(ZTarget.Top, i, stamp),
                }
        );
    }

    /// <summary>
    /// Deals <paramref name="countPerPlayer"/> cards to every seat in turn, like a real deal.
    /// When the deck runs out, the last seats get fewer. With no seats, the local player draws instead.
    /// </summary>
    private static IEnumerable<ComponentPosition> Dealt(
        IAtomReader R,
        ComponentPosition deck,
        int countPerPlayer
    )
    {
        var seats = R.Get(Seat.Live);
        if (seats.Count == 0)
            return Drawn(R, deck, countPerPlayer);

        var cards = R.TokensOn(deck);
        int total = (int)Math.Min((long)countPerPlayer * seats.Count, cards.Count);
        var stamp = SnowportId.Create();
        // Each seat's cards go into its hand in the order they're dealt.
        return cards
            .Take(total)
            .Select(
                (card, i) =>
                    PlayerHandService.MovedToHand(
                        card,
                        seats[i % seats.Count].Id,
                        i / seats.Count,
                        stamp
                    )
            );
    }

    private static int PerSeat(int cards, int seats) =>
        seats == 0 ? cards : (cards + seats - 1) / seats;
}

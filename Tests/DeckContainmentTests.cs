using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A card in a deck is contained by the deck, the way a token in a bag is.
/// </summary>
public class DeckContainmentTests
{
    private static readonly Ref<Prototype> DeckProto = new(10);
    private static readonly Ref<Prototype> BagProto = new(11);
    private static readonly SnowTag Deck = new(20);
    private static readonly SnowTag Bag = new(21);
    private static readonly SnowTag Hand = new(30);

    private static ComponentPosition State(
        SnowTag id,
        SnowTag container = default,
        int order = 0,
        float yaw = 0
    ) =>
        new()
        {
            Id = id,
            ContainerRef = container,
            ZOrder = new ZOrder(ZTarget.Top, order, new SnowportId(((long)order << 8) | 1)),
            Rotation = new Vector3(0, yaw, 0),
        };

    private static Component Card(ComponentPosition s) =>
        new()
        {
            Id = new(s.Id),
            PrototypeRef = DeckProto,
            Item = new PrintedItemRef(null, null),
        };

    // A deck of three cards, a bag with a token in it, a loose token, and a token in a hand.
    private static readonly ComponentPosition TheDeck = State(Deck);
    private static readonly ComponentPosition Bottom = State(new(2), Deck, 1);
    private static readonly ComponentPosition Middle = State(new(3), Deck, 2);
    private static readonly ComponentPosition Top = State(new(4), Deck, 3);
    private static readonly ComponentPosition TheBag = State(Bag);
    private static readonly ComponentPosition InBag = State(new(6), Bag);
    private static readonly ComponentPosition Loose = State(new(7));
    private static readonly ComponentPosition InHand = State(new(8), Hand);

    private static readonly ListReader R = new(
        new Prototype { Id = DeckProto, Parameters = new PrintedParameters() },
        new Prototype { Id = BagProto, Parameters = new BagParameters() },
        new Component { Id = new(Deck), PrototypeRef = DeckProto },
        new Component { Id = new(Bag), PrototypeRef = BagProto },
        Card(Bottom),
        Card(Middle),
        Card(Top),
        Card(InBag),
        Card(Loose),
        Card(InHand),
        TheDeck,
        Bottom,
        Middle,
        Top,
        TheBag,
        InBag,
        Loose,
        InHand
    );

    [Fact]
    public void ADecksCardsAreItsContainedComponentsTopFirst()
    {
        Assert.Equal([Top.Id, Middle.Id, Bottom.Id], R.TokensOn(TheDeck).Select(c => c.Id));
    }

    [Fact]
    public void ACardIsContainedByItsDeck()
    {
        Assert.Equal([IAtomRef.To(TheDeck)], Top.Containers(R));
        Assert.Equal(
            new[] { Bottom, Middle, Top }.Select(IAtomRef.To).ToHashSet(),
            TheDeck.Contents(R).ToHashSet()
        );
        Assert.Empty(Loose.Containers(R));
    }

    [Fact]
    public void OnlyACardInADeckIsInADeck()
    {
        Assert.True(R.IsInDeck(Top));
        Assert.False(R.IsInDeck(Loose));
        Assert.False(R.IsInDeck(InBag));
        Assert.False(R.IsInDeck(InHand));
    }
}

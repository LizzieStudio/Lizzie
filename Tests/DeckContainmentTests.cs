using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

/// <summary>
/// A card in a deck is contained by the deck, the way a token in a bag is.
/// </summary>
public class DeckContainmentTests
{
    private static readonly SnowTag DeckProto = new(10);
    private static readonly SnowTag BagProto = new(11);
    private static readonly SnowTag Deck = new(20);
    private static readonly SnowTag Bag = new(21);
    private static readonly SnowTag Hand = new(30);

    private static readonly ComponentVariant DeckVariant = new()
    {
        Id = new(40),
        PrototypeRef = DeckProto,
    };

    private static readonly ComponentVariant CardVariant = new()
    {
        Id = new(41),
        PrototypeRef = DeckProto,
        Item = new PrintedItemRef(0, SnowTag.Empty, SnowTag.Empty),
    };

    private static readonly ComponentVariant BagVariant = new()
    {
        Id = new(42),
        PrototypeRef = BagProto,
    };

    private static ComponentPosition State(
        SnowTag id,
        ComponentVariant variant,
        SnowTag container = default,
        int order = 0,
        float yaw = 0
    ) =>
        new()
        {
            Id = id,
            VariantRef = variant.Id,
            ContainerRef = container,
            ZOrder = new ZOrder(ZTarget.Top, order, new SnowportId(((long)order << 8) | 1)),
            Rotation = new Vector3(0, yaw, 0),
        };

    // A deck of three cards, a bag with a token in it, a loose token, and a token in a hand.
    private static readonly ComponentPosition TheDeck = State(Deck, DeckVariant);
    private static readonly ComponentPosition Bottom = State(new(2), CardVariant, Deck, 1);
    private static readonly ComponentPosition Middle = State(new(3), CardVariant, Deck, 2);
    private static readonly ComponentPosition Top = State(new(4), CardVariant, Deck, 3);
    private static readonly ComponentPosition TheBag = State(Bag, BagVariant);
    private static readonly ComponentPosition InBag = State(new(6), CardVariant, Bag);
    private static readonly ComponentPosition Loose = State(new(7), CardVariant);
    private static readonly ComponentPosition InHand = State(new(8), CardVariant, Hand);

    private static readonly ListReader R = new(
        new Prototype { Id = DeckProto, Parameters = new PrintedParameters() },
        new Prototype { Id = BagProto, Parameters = new BagParameters() },
        DeckVariant,
        CardVariant,
        BagVariant,
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
        Assert.Equal([AtomRef.To(TheDeck)], Top.Containers(R));
        Assert.Equal(
            new[] { Bottom, Middle, Top }.Select(AtomRef.To).ToHashSet(),
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

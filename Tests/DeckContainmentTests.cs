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

    /// <summary>Reads from a fixed list of atoms.</summary>
    private sealed class Reader(params Atom[] atoms) : IAtomReader
    {
        public Atom Get(AtomRef atomRef) => throw new NotSupportedException();

        public SnowportId Version(Atom atom) => SnowportId.Empty;

        public T Get<T, I>(Id<T, I> byId, I id)
            where T : Atom
            where I : IComparable<I> =>
            atoms.OfType<T>().FirstOrDefault(a => byId.Of(a).Equals(id));

        public IReadOnlyList<T> Get<T, G>(Group<T, G> group, G key)
            where T : Atom => atoms.OfType<T>().Where(a => Equals(group.Of(a), key)).ToList();

        public int Count<T, G>(Group<T, G> group, G key)
            where T : Atom => Get(group, key).Count;
    }

    private static ComponentState State(
        SnowTag id,
        SnowTag proto,
        SnowTag container = default,
        int order = 0,
        float yaw = 0,
        bool item = false
    ) =>
        new()
        {
            Id = id,
            PrototypeRef = proto,
            ContainerRef = container,
            ZOrder = new ZOrder(ZTarget.Top, order, new SnowportId(((long)order << 8) | 1)),
            Rotation = new Vector3(0, yaw, 0),
            Card = item
                ? new PrototypeItemRef(order, SnowTag.Empty, SnowTag.Empty)
                : PrototypeItemRef.None,
        };

    // A deck of three cards, a bag with a token in it, a loose token, and a token in a hand.
    private static readonly ComponentState TheDeck = State(Deck, DeckProto);
    private static readonly ComponentState Bottom = State(new(2), DeckProto, Deck, 1, item: true);
    private static readonly ComponentState Middle = State(new(3), DeckProto, Deck, 2, item: true);
    private static readonly ComponentState Top = State(new(4), DeckProto, Deck, 3, item: true);
    private static readonly ComponentState TheBag = State(Bag, BagProto);
    private static readonly ComponentState InBag = State(new(6), DeckProto, Bag, item: true);
    private static readonly ComponentState Loose = State(new(7), DeckProto, item: true);
    private static readonly ComponentState InHand = State(new(8), DeckProto, Hand, item: true);

    private static readonly Reader R = new(
        new Prototype { Id = DeckProto, Parameters = new DeckParameters() },
        new Prototype { Id = BagProto, Parameters = new BagParameters() },
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
        Assert.Equal([ComponentState.ById.Ref(Deck)], Top.Containers(R));
        Assert.Equal(
            new[] { Bottom, Middle, Top }.Select(c => ComponentState.ById.Ref(c.Id)).ToHashSet(),
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

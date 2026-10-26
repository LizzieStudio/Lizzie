using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A deck keeps the cards its prototype still has, updates a card when one side still matches,
/// retires the rest, and issues the cards it's missing in the blueprint where it first appears.
/// Blueprints remove retired cards wherever they are, and tables keep the cards they moved.
/// </summary>
public class DeckReconciliationTests
{
    private static readonly Ref<Prototype> Proto = new(1);
    private static readonly Ref<Stage> Root = new(2),
        Child = new(3);
    private static readonly Ref<Component> Deck = new(4);
    private static readonly SnowTag Numbers = new(6),
        Backs = new(7);

    private static QuickFace Number(string value) => new(Numbers, value);

    private static PrintedItemRef Card(string value) =>
        new(Number(value), new QuickFace(Backs, "B"));

    private static PrintedParameters Cards(string values) =>
        new()
        {
            Front = new QuickSide { Suits = [new QuickSuit { Id = Numbers, Values = values }] },
            Back = new QuickSide { Suits = [new QuickSuit { Id = Backs, Values = "B" }] },
        };

    /// <summary>The stages, the deck in the root, and a card per value, each in the deck unless it's in <paramref name="onTable"/>.</summary>
    private static List<Atom> Table(string[] values, params int[] onTable)
    {
        var atoms = new List<Atom>
        {
            new Stage { Id = Root },
            new Stage { Id = Child, ParentRef = Root },
            new Component { Id = Deck, PrototypeRef = Proto },
            new ComponentPosition { Id = Deck, StageRef = Root },
        };
        for (int i = 0; i < values.Length; i++)
        {
            atoms.Add(
                new Component
                {
                    Id = new(20 + i),
                    PrototypeRef = Proto,
                    Item = Card(values[i]),
                    DeckRef = Deck,
                }
            );
            atoms.Add(
                new ComponentPosition
                {
                    Id = new(20 + i),
                    StageRef = Root,
                    ContainerRef = onTable.Contains(i) ? SnowTag.Empty : Deck.Tag,
                }
            );
        }
        return atoms;
    }

    /// <summary>Makes <paramref name="stage"/> a table.</summary>
    private static List<Atom> AsTable(List<Atom> atoms, Ref<Stage> stage) =>
        [.. atoms.Select(a => a is Stage s && s.Id == stage ? s with { IsTable = true } : a)];

    /// <summary>Moves card <paramref name="card"/> onto the table in <paramref name="stage"/>, as a write of its own.</summary>
    private static List<Atom> MovedIn(List<Atom> atoms, Ref<Stage> stage, int card) =>
        [.. atoms, new ComponentPosition { Id = new(20 + card), StageRef = stage }];

    /// <summary>Retires card <paramref name="card"/>.</summary>
    private static List<Atom> Retired(List<Atom> atoms, int card) =>
        [
            .. atoms.Select(a =>
                a is Component c && c.Id == new Ref<Component>(20 + card)
                    ? c with
                    {
                        Retired = true,
                    }
                    : a
            ),
        ];

    private static List<IChange> Reconcile(List<Atom> atoms, string values)
    {
        int next = 100;
        return DeckReconciliation.Changes(
            new ListReader([.. atoms]),
            Proto,
            Cards(values),
            () => new Ref<Component>(next++),
            new SnowportId(1 << 8)
        );
    }

    [Fact]
    public void ALookChangeChangesNoCards()
    {
        var changes = Reconcile(Table(["1", "2"]), "1-2");

        Assert.True(changes.Count == 0, $"Reconciling the same cards wrote {Describe(changes)}.");
    }

    [Fact]
    public void ANewValueIsIssuedAtTheBottomOfTheDeck()
    {
        var changes = Reconcile(Table(["1", "2"]), "1-3");

        var component = Assert.Single(changes.OfType<Component>());
        var position = Assert.Single(changes.OfType<ComponentPosition>());
        Assert.True(
            Equals(component.Item, Card("3"))
                && component.DeckRef == Deck
                && position.Id == component.Id
                && position.ContainerRef == Deck.Tag
                && position.StageRef == Root
                && position.ZOrder.Target == ZTarget.Bottom,
            $"Adding 3 wrote {Describe(changes)}, expected card 3 at the bottom of the deck in the root."
        );
    }

    [Fact]
    public void AGoneValueIsRetiredAndRemoved()
    {
        var changes = Reconcile(Table(["1", "2"]), "1");

        AssertWrites(changes, retired: [21], removed: [(Root, 21)]);
    }

    [Fact]
    public void TheDeckRetiresTheCopyItHolds()
    {
        // Two 1s: the first is on the table, the second is in the deck.
        var changes = Reconcile(Table(["1", "1"], onTable: 0), "1");

        AssertWrites(changes, retired: [21], removed: [(Root, 21)]);
    }

    [Fact]
    public void ABlueprintRemovesARetiredCardWhereverItIs()
    {
        var changes = Reconcile(Table(["1", "2"], onTable: 1), "1");

        AssertWrites(changes, retired: [21], removed: [(Root, 21)]);
    }

    [Fact]
    public void ARetiredCardPutBackIsRemovedNextTime()
    {
        var changes = Reconcile(Retired(Table(["1", "2"]), 1), "1");

        AssertWrites(changes, retired: [], removed: [(Root, 21)]);
    }

    [Fact]
    public void EveryBlueprintThatMovedARetiredCardRemovesIt()
    {
        var changes = Reconcile(MovedIn(Table(["1", "2"]), Child, 1), "1");

        AssertWrites(changes, retired: [21], removed: [(Root, 21), (Child, 21)]);
    }

    [Fact]
    public void ATableKeepsTheRetiredCardsItMoved()
    {
        var changes = Reconcile(AsTable(MovedIn(Table(["1", "2"]), Child, 1), Child), "1");

        AssertWrites(changes, retired: [21], removed: [(Root, 21)]);
    }

    [Fact]
    public void ADeckFirstOnATableIsLeftAlone()
    {
        var changes = Reconcile(AsTable(Table(["1", "2"]), Root), "1-3");

        Assert.True(changes.Count == 0, $"Reconciling a table's deck wrote {Describe(changes)}.");
    }

    [Fact]
    public void EqualCardsPairFirst()
    {
        var (pairs, unwanted, missing) = DeckReconciliation.Match(
            [Card("1"), Card("2")],
            [Card("2"), Card("1")]
        );

        Assert.True(
            Describe(pairs) == "0-1, 1-0" && unwanted.Count == 0 && missing.Count == 0,
            $"Paired {Describe(pairs)}, expected 0-1, 1-0 with nothing left."
        );
    }

    [Fact]
    public void AnUnchangedFrontKeepsTheCard()
    {
        var (pairs, _, _) = DeckReconciliation.Match(
            [new PrintedItemRef(Number("1"), Number("A"))],
            [new PrintedItemRef(Number("1"), Number("Z"))]
        );

        Assert.True(
            Describe(pairs) == "0-0",
            $"Paired {Describe(pairs)}, expected 0-0 by the front."
        );
    }

    [Fact]
    public void AnUnchangedBackKeepsTheCard()
    {
        var (pairs, _, _) = DeckReconciliation.Match(
            [new PrintedItemRef(Number("1"), Number("A"))],
            [new PrintedItemRef(Number("9"), Number("A"))]
        );

        Assert.True(
            Describe(pairs) == "0-0",
            $"Paired {Describe(pairs)}, expected 0-0 by the back."
        );
    }

    [Fact]
    public void ASharedBackKeepsNoCard()
    {
        // 1-3 becomes 2-4 under one back: 1 goes and 4 comes, rather than 1 turning into 4.
        var (pairs, unwanted, missing) = DeckReconciliation.Match(
            [Card("1"), Card("2"), Card("3")],
            [Card("2"), Card("3"), Card("4")]
        );

        Assert.True(
            Describe(pairs) == "1-0, 2-1"
                && unwanted.SequenceEqual([0])
                && missing.SequenceEqual([2]),
            $"Paired {Describe(pairs)}, leaving issued [{string.Join(", ", unwanted)}] and wanted "
                + $"[{string.Join(", ", missing)}]; expected 1-0, 2-1, leaving issued [0] and wanted [2]."
        );
    }

    [Fact]
    public void TheFrontWinsOverTheBack()
    {
        // Card 0's front matches wanted 0, and its back matches wanted 1.
        var (pairs, _, _) = DeckReconciliation.Match(
            [new PrintedItemRef(Number("1"), Number("A"))],
            [
                new PrintedItemRef(Number("1"), Number("Z")),
                new PrintedItemRef(Number("9"), Number("A")),
            ]
        );

        Assert.True(
            Describe(pairs) == "0-0",
            $"Paired {Describe(pairs)}, expected 0-0 by the front."
        );
    }

    [StackTraceHidden]
    private static void AssertWrites(
        List<IChange> changes,
        int[] retired,
        (Ref<Stage> Stage, int Id)[] removed
    )
    {
        var actualRetired = changes
            .OfType<Component>()
            .Where(c => c.Retired)
            .Select(c => c.Id)
            .Order();
        var actualRemoved = changes
            .OfType<ComponentPosition>()
            .Where(p => p.Removed)
            .Select(p => (p.StageRef, p.Id))
            .Order();
        Assert.True(
            changes.Count == retired.Length + removed.Length
                && actualRetired.SequenceEqual(retired.Select(i => new Ref<Component>(i)))
                && actualRemoved.SequenceEqual(
                    removed.Select(r => (r.Stage, new Ref<Component>(r.Id))).Order()
                ),
            $"Wrote {Describe(changes)}.\n\nExpected only: components [{string.Join(", ", retired)}] retired, "
                + $"and positions [{string.Join(", ", removed)}] removed, as (stage, position)."
        );
    }

    private static string Describe(List<IChange> changes) =>
        changes.Count == 0 ? "nothing" : string.Join("; ", changes);

    private static string Describe(List<(int Issued, int Wanted)> pairs) =>
        string.Join(", ", pairs.Select(p => $"{p.Issued}-{p.Wanted}"));
}

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
        Child = new(3),
        Middle = new(5);
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
        var ranks = SnowRank.AfterAll(values.Length);
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
                    Location = onTable.Contains(i) ? null : new InComponent(Deck),
                    Rank = ranks[i],
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

    /// <summary>
    /// Puts a blueprint between the root and the child that removed the deck, and has the child write it again,
    /// as each blueprint shows them.
    /// </summary>
    private static List<Atom> BroughtBackBelowARemoval(List<Atom> atoms) =>
        [
            .. atoms.Select(a => a is Stage s && s.Id == Child ? s with { ParentRef = Middle } : a),
            new Stage { Id = Middle, ParentRef = Root },
            .. atoms
                .OfType<ComponentPosition>()
                .Where(p => p.StageRef == Root)
                .Select(p => p with { StageRef = Child }),
        ];

    /// <summary>
    /// Has the child write the deck again under a blueprint that shows it only as removed, with nothing above showing it:
    /// the root keeps the deck in a bag it removed, and the middle blueprint wrote the bag again but removed the deck.
    /// Gives what each blueprint shows, and which of that shows only because removed components are shown.
    /// </summary>
    private static (List<Atom> Atoms, ComponentPosition[] Removed) ShownRemovedAbove(
        List<Atom> atoms
    )
    {
        var root = atoms.OfType<ComponentPosition>().Where(p => p.StageRef == Root).ToList();
        ComponentPosition[] middle = [.. root.Select(p => p with { StageRef = Middle })];
        return (
            [
                .. atoms
                    .Except(root)
                    .Select(a => a is Stage s && s.Id == Child ? s with { ParentRef = Middle } : a),
                new Stage { Id = Middle, ParentRef = Root },
                .. middle,
                .. root.Select(p => p with { StageRef = Child }),
            ],
            middle
        );
    }

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

    /// <param name="removed">The positions that show only because removed components are shown.</param>
    private static List<IChange> Reconcile(
        List<Atom> atoms,
        string values,
        params ComponentPosition[] removed
    ) =>
        DeckReconciliation.Changes(
            new ListReader([.. atoms]) { Removed = removed },
            Proto,
            Cards(values)
        );

    [Fact]
    public void ALookChangeChangesNoCards()
    {
        var changes = Reconcile(Table(["1", "2"]), "1-2");

        Assert.True(changes.Count == 0, $"Reconciling the same cards wrote {Describe(changes)}.");
    }

    [Fact]
    public void ANewValueIsIssuedAtTheBottomOfTheDeck()
    {
        var table = Table(["1", "2"]);
        var changes = Reconcile(table, "1-3");

        var component = Assert.Single(changes.OfType<Component>());
        var position = Assert.Single(changes.OfType<ComponentPosition>());
        var bottom = table.OfType<ComponentPosition>().Where(p => p.Id != Deck).Min(p => p.Rank);
        Assert.True(
            Equals(component.Item, Card("3"))
                && component.DeckRef == Deck
                && position.Id == component.Id
                && position.Location == new InComponent(Deck)
                && position.StageRef == Root
                && position.Rank.CompareTo(bottom) < 0,
            $"Adding 3 wrote {Describe(changes)}, expected card 3 at the bottom of the deck in the root."
        );
    }

    [Fact]
    public void AGoneValueIsRetiredAndRemoved()
    {
        var changes = Reconcile(Table(["1", "2"]), "1");

        AssertWrites(changes, retired: [21], unset: [(Root, 21)]);
    }

    [Fact]
    public void TheDeckRetiresTheCopyItHolds()
    {
        // Two 1s: the first is on the table, the second is in the deck.
        var changes = Reconcile(Table(["1", "1"], onTable: 0), "1");

        AssertWrites(changes, retired: [21], unset: [(Root, 21)]);
    }

    [Fact]
    public void ABlueprintRemovesARetiredCardWhereverItIs()
    {
        var changes = Reconcile(Table(["1", "2"], onTable: 1), "1");

        AssertWrites(changes, retired: [21], unset: [(Root, 21)]);
    }

    [Fact]
    public void ARetiredCardPutBackIsRemovedNextTime()
    {
        var changes = Reconcile(Retired(Table(["1", "2"]), 1), "1");

        AssertWrites(changes, retired: [], unset: [(Root, 21)]);
    }

    [Fact]
    public void EveryBlueprintThatMovedARetiredCardRemovesIt()
    {
        var changes = Reconcile(MovedIn(Table(["1", "2"]), Child, 1), "1");

        AssertWrites(changes, retired: [21], unset: [(Root, 21), (Child, 21)]);
    }

    [Fact]
    public void ATableKeepsTheRetiredCardsItMoved()
    {
        var changes = Reconcile(AsTable(MovedIn(Table(["1", "2"]), Child, 1), Child), "1");

        AssertWrites(changes, retired: [21], unset: [(Root, 21)]);
    }

    [Fact]
    public void ADeckBroughtBackBelowARemovalGetsItsCardsFromAbove()
    {
        var changes = Reconcile(BroughtBackBelowARemoval(Table(["1", "2"])), "1-3");

        var positions = changes.OfType<ComponentPosition>().ToList();
        Assert.True(
            positions.Count == 1 && positions[0].StageRef == Root,
            $"Adding 3 wrote {Describe(changes)}, expected one card, in the root."
        );
    }

    [Fact]
    public void ADeckShownOnlyAsRemovedAboveGetsItsCardsBelow()
    {
        var (atoms, removed) = ShownRemovedAbove(Table(["1", "2"]));
        var changes = Reconcile(atoms, "1-3", removed);

        var positions = changes.OfType<ComponentPosition>().ToList();
        Assert.True(
            positions.Count == 1 && positions[0].StageRef == Child,
            $"Adding 3 wrote {Describe(changes)}, expected one card, in the child."
        );
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
        (Ref<Stage> Stage, int Id)[] unset
    )
    {
        var actualRetired = changes
            .OfType<Component>()
            .Where(c => c.Retired)
            .Select(c => c.Id)
            .Order();
        var actualUnset = changes
            .OfType<Unset>()
            .Where(u => u.Query == ComponentPosition.ById)
            .Select(u => (PositionKey)u.Key)
            .Select(k => (k.StageRef, k.ComponentRef))
            .Order();
        Assert.True(
            changes.Count == retired.Length + unset.Length
                && actualRetired.SequenceEqual(retired.Select(i => new Ref<Component>(i)))
                && actualUnset.SequenceEqual(
                    unset.Select(r => (r.Stage, new Ref<Component>(r.Id))).Order()
                ),
            $"Wrote {Describe(changes)}.\n\nExpected only: components [{string.Join(", ", retired)}] retired, "
                + $"and positions [{string.Join(", ", unset)}] unset, as (stage, position)."
        );
    }

    private static string Describe(List<IChange> changes) =>
        changes.Count == 0 ? "nothing" : string.Join("; ", changes);

    private static string Describe(List<(int Issued, int Wanted)> pairs) =>
        string.Join(", ", pairs.Select(p => $"{p.Issued}-{p.Wanted}"));
}

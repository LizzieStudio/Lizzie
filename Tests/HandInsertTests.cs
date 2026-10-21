using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

public class HandInsertTests
{
    private static readonly SnowTag Hand = new(100);

    // Cards 1 to n already in the hand, in order, stacked by older stamps.
    private static List<ComponentPosition> HandOf(int n) =>
        Enumerable
            .Range(1, n)
            .Select(i => new ComponentPosition
            {
                Id = new SnowTag(i),
                ContainerRef = Hand,
                ZOrder = new ZOrder(ZTarget.Top, 0, new SnowportId(((long)i << 8) | 1)),
            })
            .ToList();

    private static ComponentPosition Card(int id) => new() { Id = new SnowTag(id) };

    // The hand's order once the atoms are applied.
    private static int[] Order(List<ComponentPosition> hand, IEnumerable<ComponentPosition> written)
    {
        var all = hand.ToDictionary(c => c.Id);
        foreach (var c in written)
            all[c.Id] = c;
        return all
            .Values.Where(c => c.ContainerRef == Hand)
            .OrderBy(c => c.ZOrder)
            .Select(c => (int)c.Id)
            .ToArray();
    }

    private static readonly SnowportId Stamp = new((1000L << 8) | 1);

    [Fact]
    public void ACardGoesWhereItsDropped()
    {
        var hand = HandOf(4);
        var written = PlayerHandService.Inserted(hand, [Card(9)], 2, Hand, Stamp).ToList();

        Assert.Equal(new[] { 1, 2, 9, 3, 4 }, Order(hand, written));
        // The cards before the drop keep their order without being written.
        Assert.Equal(new[] { 9, 3, 4 }, written.Select(c => (int)c.Id));
    }

    [Fact]
    public void ACardMovesWithinItsHand()
    {
        var hand = HandOf(4);
        var written = PlayerHandService.Inserted(hand, [hand[3]], 0, Hand, Stamp);

        Assert.Equal(new[] { 4, 1, 2, 3 }, Order(hand, written));
    }

    [Fact]
    public void SeveralCardsKeepTheirOrder()
    {
        var hand = HandOf(3);
        var written = PlayerHandService.Inserted(hand, [Card(8), Card(9)], 1, Hand, Stamp);

        Assert.Equal(new[] { 1, 8, 9, 2, 3 }, Order(hand, written));
    }

    [Fact]
    public void AnIndexPastTheEndAppends()
    {
        var hand = HandOf(2);
        var written = PlayerHandService.Inserted(hand, [Card(9)], 10, Hand, Stamp).ToList();

        Assert.Equal(new[] { 1, 2, 9 }, Order(hand, written));
        Assert.Single(written);
    }
}

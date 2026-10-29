using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace Lizzie.Tests;

public class HandInsertTests
{
    private static readonly Ref<Seat> Seat = new(100);

    // Cards 1 to n already in the hand, in order, stacked by older stamps.
    private static List<ComponentPosition> HandOf(int n) =>
        Enumerable
            .Range(1, n)
            .Select(i => new ComponentPosition
            {
                Id = new(i),
                Container = new InHand(Seat),
                ZOrder = new ZOrder(ZTarget.Top, 0, new SnowportId(((long)i << 8) | 1)),
            })
            .ToList();

    private static ComponentPosition Card(int id) => new() { Id = new(id) };

    // The hand's order once the atoms are applied.
    private static int[] Order(List<ComponentPosition> hand, IEnumerable<ComponentPosition> written)
    {
        var all = hand.ToDictionary(c => c.Id);
        foreach (var c in written)
            all[c.Id] = c;
        return all
            .Values.Where(c => c.Container == new InHand(Seat))
            .OrderBy(c => c.ZOrder)
            .Select(c => (int)c.Id.Tag)
            .ToArray();
    }

    private static readonly SnowportId Stamp = new((1000L << 8) | 1);

    [Fact]
    public void ACardGoesWhereItsDropped()
    {
        var hand = HandOf(4);
        var written = PlayerHandService.Inserted(hand, [Card(9)], 2, Seat, Stamp).ToList();

        Assert.Equal(new[] { 1, 2, 9, 3, 4 }, Order(hand, written));
        // The cards before the drop keep their order without being written.
        Assert.Equal(new[] { 9, 3, 4 }, written.Select(c => (int)c.Id.Tag));
    }

    [Fact]
    public void ACardMovesWithinItsHand()
    {
        var hand = HandOf(4);
        var written = PlayerHandService.Inserted(hand, [hand[3]], 0, Seat, Stamp);

        Assert.Equal(new[] { 4, 1, 2, 3 }, Order(hand, written));
    }

    [Fact]
    public void SeveralCardsKeepTheirOrder()
    {
        var hand = HandOf(3);
        var written = PlayerHandService.Inserted(hand, [Card(8), Card(9)], 1, Seat, Stamp);

        Assert.Equal(new[] { 1, 8, 9, 2, 3 }, Order(hand, written));
    }

    [Fact]
    public void AnIndexPastTheEndAppends()
    {
        var hand = HandOf(2);
        var written = PlayerHandService.Inserted(hand, [Card(9)], 10, Seat, Stamp).ToList();

        Assert.Equal(new[] { 1, 2, 9 }, Order(hand, written));
        Assert.Single(written);
    }

    [Fact]
    public void ADrawnOrDealtCardArrivesUprightAndFaceUp()
    {
        var faceDown = new ComponentPosition { Id = new(9), Rotation = new(0, 1, Mathf.Pi) };
        var moved = PlayerHandService.MovedToHand(faceDown, Seat, 0, Stamp);

        Assert.Equal(Vector3.Zero, moved.Rotation);
    }
}

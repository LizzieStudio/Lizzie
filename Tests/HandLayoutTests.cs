using System.Linq;
using Xunit;

namespace Lizzie.Tests;

public class HandLayoutTests
{
    private const float Separation = 6;

    [Fact]
    public void CardsThatFitSitSideBySideCentered()
    {
        var layout = HandLayout.Place([50, 50, 50], 300, Separation);

        // 3 cards and 2 separations take 162 of 300, leaving 69 each side.
        Assert.Equal(new[] { 69f, 125f, 181f }, layout.Lefts);
    }

    [Fact]
    public void TooManyCardsOverlapWithinTheRow()
    {
        var widths = Enumerable.Repeat(50f, 20).ToArray();
        var layout = HandLayout.Place(widths, 300, Separation);

        Assert.Equal(0, layout.Lefts[0]);
        Assert.Equal(300, layout.Lefts[^1] + widths[^1], 3);
        for (int i = 1; i < widths.Length; i++)
            Assert.True(
                layout.Lefts[i] < layout.Lefts[i - 1] + widths[i - 1],
                $"card {i} doesn't overlap"
            );
    }

    [Fact]
    public void TheHoveredCardShowsWhole()
    {
        var widths = Enumerable.Repeat(50f, 20).ToArray();
        var layout = HandLayout.Place(widths, 300, Separation, hovered: 7);

        Assert.True(layout.Lefts[8] >= layout.Lefts[7] + widths[7]);
        Assert.Equal(300, layout.Lefts[^1] + widths[^1], 3);
        Assert.Equal(7, layout.CardAt(layout.Lefts[7] + 25));
    }

    [Fact]
    public void AGapOpensBeforeTheDropIndex()
    {
        var without = HandLayout.Place([50, 50, 50], 400, Separation);
        var with = HandLayout.Place([50, 50, 50], 400, Separation, gapAt: 1, gapWidth: 50);

        Assert.Equal(
            56,
            (with.Lefts[1] - with.Lefts[0]) - (without.Lefts[1] - without.Lefts[0]),
            3
        );
        Assert.Equal(without.Lefts[2] - without.Lefts[1], with.Lefts[2] - with.Lefts[1], 3);
    }

    [Fact]
    public void AGapStillFitsWhenCardsOverlap()
    {
        var widths = Enumerable.Repeat(50f, 20).ToArray();
        var layout = HandLayout.Place(widths, 300, Separation, gapAt: 20, gapWidth: 50);

        Assert.Equal(300 - 56, layout.Lefts[^1] + widths[^1], 3);
    }

    [Fact]
    public void CardAtFindsTheCardOnTop()
    {
        var widths = Enumerable.Repeat(50f, 20).ToArray();
        var layout = HandLayout.Place(widths, 300, Separation);

        // Each card covers the right side of the one before it.
        Assert.Equal(1, layout.CardAt(layout.Lefts[1] + 1));
        Assert.Equal(19, layout.CardAt(299));
        Assert.Equal(-1, layout.CardAt(301));
    }

    [Fact]
    public void InsertIndexCountsTheCardsLeftOfThePoint()
    {
        var layout = HandLayout.Place([50, 50, 50], 300, Separation);

        Assert.Equal(0, layout.InsertIndexAt(0));
        Assert.Equal(1, layout.InsertIndexAt(layout.Lefts[1]));
        Assert.Equal(3, layout.InsertIndexAt(300));
    }
}

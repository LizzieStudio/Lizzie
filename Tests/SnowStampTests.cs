using System;
using Xunit;

namespace Lizzie.Tests;

public class SnowStampTests
{
    [Fact]
    public void IdKeepsItsClockAndSource()
    {
        var id = new SnowStamp(1234, 5);

        Assert.Equal(1234, id.LogicClock);
        Assert.Equal(5, id.Source);
        Assert.Equal((1234L << 6) | 5, (long)id);
    }

    [Fact]
    public void TagKeepsItsSourceAndCounter()
    {
        var tag = new SnowTag(40, 99);

        Assert.Equal(40, tag.Source);
        Assert.Equal(99, tag.Counter);
        Assert.True(tag < 0, "A tag from source 32 or up should be negative.");
    }

    [Fact]
    public void TagCounterCantSpillIntoTheSource() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowTag(1, SnowTag.MaxCounter + 1));

    [Fact]
    public void SourceMustFitInSixBits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowStamp(1, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowTag(64, 1));
    }

    [Fact]
    public void IdsCompareByClockThenSource()
    {
        var early = new SnowStamp(10, 9);
        var late = new SnowStamp(11, 1);

        Assert.True(early < late);
        Assert.True(late > early);
        Assert.True(early <= new SnowStamp(10, 9));
        Assert.True(new SnowStamp(10, 2) >= new SnowStamp(10, 1));
    }

    [Fact]
    public void IdConvertsToUlongUnchanged()
    {
        var id = new SnowStamp(1L << 46, 63);
        ulong value = id;

        Assert.Equal((ulong)(long)id, value);
    }

    [Fact]
    public void FormatStringsApplyToTheNumber()
    {
        Assert.Equal("000000FF", $"{new SnowTag(255):X8}");
        Assert.Equal("0000000000000041", $"{new SnowStamp(1, 1):X16}");
        Assert.Equal("255", new SnowTag(255).ToString());
    }
}

using System;
using Xunit;

namespace Lizzie.Tests;

public class SnowStampTests
{
    [Fact]
    public void IdKeepsItsClockAndSource()
    {
        var id = new SnowStamp(1234, new Source(5));

        Assert.Equal(1234, id.LogicClock);
        Assert.Equal(new Source(5), id.Source);
        Assert.Equal((1234L << 6) | 5, (long)id);
    }

    [Fact]
    public void TagKeepsItsSourceAndCounter()
    {
        var tag = new SnowTag(new Source(40), 99);

        Assert.Equal(new Source(40), tag.Source);
        Assert.Equal(99, tag.Counter);
        Assert.True(tag < 0, "A tag from source 32 or up should be negative.");
    }

    [Fact]
    public void TagCounterCantSpillIntoTheSource() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SnowTag(Source.Host, SnowTag.MaxCounter + 1)
        );

    [Fact]
    public void SourceMustFitInSixBits() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Source(64));

    [Fact]
    public void IdsCompareByClockThenSource()
    {
        var early = new SnowStamp(10, new Source(9));
        var late = new SnowStamp(11, Source.Host);

        Assert.True(early < late);
        Assert.True(late > early);
        Assert.True(early <= new SnowStamp(10, new Source(9)));
        Assert.True(new SnowStamp(10, new Source(2)) >= new SnowStamp(10, Source.Host));
    }

    [Fact]
    public void FormatStringsApplyToTheNumber()
    {
        Assert.Equal("000000FF", $"{new SnowTag(255):X8}");
        Assert.Equal("0000000000000041", $"{new SnowStamp(1, Source.Host):X16}");
        Assert.Equal("255", new SnowTag(255).ToString());
    }
}

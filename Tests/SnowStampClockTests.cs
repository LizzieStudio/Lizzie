using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

[Collection(ClockSwaps.Name)]
public class SnowStampClockTests
{
    [Fact]
    public void IdsKeepIncreasing()
    {
        // More ids than fit in a millisecond, then time moves on.
        long now = 0;
        SnowStamp.TicksMsec = () => now;
        var ids = new List<SnowStamp>();
        try
        {
            for (int i = 0; i < 500; i++)
            {
                ids.Add(SnowStamp.Create());
                if (i % 100 == 99)
                    now += 1;
            }
        }
        finally
        {
            TestClock.Use();
        }

        int back = ids.Zip(ids.Skip(1)).ToList().FindIndex(p => p.First.CompareTo(p.Second) >= 0);
        if (back >= 0)
            Assert.Fail(
                $"Id {back + 1}, {ids[back + 1]}, isn't newer than id {back}, {ids[back]}."
            );
    }

    [Fact]
    public void AnIdMadeAfterANewerOneArrivesIsNewer()
    {
        var remote = new SnowStamp(SnowStamp.Create().LogicClock + 1_000_000, new Source(2));

        SnowStamp.Observe(remote);
        var next = SnowStamp.Create();

        Assert.True(next.CompareTo(remote) > 0, $"After {remote} arrived, the clock made {next}.");
    }

    [Fact]
    public void AnIdMadeAfterAnotherPlayersClockArrivesIsNewer()
    {
        long host = SnowStamp.Clock + 1_000_000;

        SnowStamp.Observe(host);
        var next = SnowStamp.Create();

        Assert.True(
            next.LogicClock > host,
            $"After the clock {host} arrived, the clock made a stamp at {next.LogicClock}."
        );
    }

    [Fact]
    public void ReadingTheClockDoesNotAdvanceIt() => Assert.Equal(SnowStamp.Clock, SnowStamp.Clock);
}

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

public class SnowportTests
{
    [Fact]
    public void IdsKeepIncreasing()
    {
        // More ids than fit in a millisecond, then time moves on.
        long now = 0;
        var clock = new Snowport(Snowport.HostSource, () => now);
        var ids = new List<SnowportId>();
        for (int i = 0; i < 500; i++)
        {
            ids.Add(clock.Create());
            if (i % 100 == 99)
                now += 1;
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
        var clock = new Snowport(Snowport.HostSource, () => 0);
        var remote = new SnowportId(1_000_000, 2);

        clock.Process(remote);
        var next = clock.Create();

        Assert.True(next.CompareTo(remote) > 0, $"After {remote} arrived, the clock made {next}.");
    }
}

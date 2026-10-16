using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

public class SnowRankTests
{
    private static int Compare(SnowRank a, SnowRank b) => a.CompareTo(b);

    // A stamp from the hybrid clock, which never repeats.
    private static long _clock = 1;

    private static SnowportId Stamp(byte source = 1) => new(_clock++, source);

    [Fact]
    public void NewRankIsBetweenItsNeighbors()
    {
        // Inserts at random places, the way rows are inserted above and below each other.
        var random = new Random(7);
        var ranks = new List<SnowRank> { SnowRank.New(default, default, Stamp()) };
        for (int i = 0; i < 2000; i++)
        {
            int at = random.Next(ranks.Count + 1);
            var prev = at > 0 ? ranks[at - 1] : default;
            var next = at < ranks.Count ? ranks[at] : default;
            ranks.Insert(
                at,
                SnowRank.New(prev, next, Stamp((byte)random.Next(Snowport.SourceCount)))
            );
        }

        for (int i = 1; i < ranks.Count; i++)
            Assert.True(Compare(ranks[i - 1], ranks[i]) < 0, $"{ranks[i - 1]} !< {ranks[i]}");
    }

    [Fact]
    public void PlayersInsertingInTheSamePlaceGetDifferentRanks()
    {
        var prev = SnowRank.New(default, default, Stamp());
        var next = SnowRank.New(prev, default, Stamp());
        long at = _clock++;

        var ranks = Enumerable
            .Range(0, Snowport.SourceCount)
            .Select(s => SnowRank.New(prev, next, new SnowportId(at, (byte)s)))
            .ToList();

        Assert.Equal(ranks.Count, ranks.Distinct().Count());
        Assert.All(ranks, r => Assert.True(Compare(prev, r) < 0 && Compare(r, next) < 0));
    }

    [Fact]
    public void RanksMadeInTheSamePlaceAtDifferentTimesDiffer()
    {
        // A row whose creation is undone, then another inserted where it was, then the first redone.
        var prev = SnowRank.New(default, default, Stamp());
        var next = SnowRank.New(prev, default, Stamp());

        var undone = SnowRank.New(prev, next, Stamp());
        var inserted = SnowRank.New(prev, next, Stamp());

        Assert.NotEqual(undone, inserted);
    }

    [Fact]
    public void ARankFitsBetweenRanksMadeInTheSamePlace()
    {
        var prev = SnowRank.New(default, default, Stamp());
        var next = SnowRank.New(prev, default, Stamp());
        var ranks = Enumerable
            .Range(0, 100)
            .Select(_ => SnowRank.New(prev, next, Stamp((byte)(_clock % 64))))
            .Order()
            .ToList();

        for (int i = 1; i < ranks.Count; i++)
        {
            var between = SnowRank.New(ranks[i - 1], ranks[i], Stamp());
            Assert.True(Compare(ranks[i - 1], between) < 0 && Compare(between, ranks[i]) < 0);
        }
    }

    [Fact]
    public void RankFitsAfterARankItPrefixes()
    {
        // The admin source's stamps start with the lowest stamp character, which leaves room below the next rank.
        var a = SnowRank.New(default, default, Stamp(0));
        var b = SnowRank.New(a, default, Stamp(5));
        var c = SnowRank.New(a, b, Stamp(0));
        var d = SnowRank.New(c, b, Stamp(0));
        var e = SnowRank.New(a, c, Stamp(63));

        Assert.True(Compare(a, e) < 0 && Compare(e, c) < 0);
        Assert.True(Compare(c, d) < 0 && Compare(d, b) < 0);
    }

    [Fact]
    public void ARankSavesAsItsString()
    {
        var rank = SnowRank.New(default, default, Stamp());
        var json = JsonSerializer.Serialize(rank, LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<SnowRank>(json, LizzieJson.EventOptions);
        Assert.True(
            json == $"\"{rank}\"" && back == rank,
            $"{rank} saved as {json} and loaded as {back}."
        );
    }

    private sealed record Item(SnowRank Rank) : IRanked;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AtPutsANewRankAtItsIndex(int index)
    {
        var items = new List<IRanked>();
        SnowRank rank = default;
        for (int i = 0; i < 3; i++)
            items.Add(new Item(rank = SnowRank.New(rank, default, Stamp())));

        items.Insert(index, new Item(SnowRank.At(items, index, Stamp())));

        var sorted = items.OrderBy(i => i.Rank).ToList();
        Assert.True(
            sorted.SequenceEqual(items),
            $"A rank made at {index} sorted to {sorted.IndexOf(items[index])}."
        );
    }

    [Fact]
    public void AppendedRanksKeepIncreasing()
    {
        SnowRank rank = default;
        for (int i = 0; i < 1000; i++)
        {
            var next = SnowRank.New(rank, default, Stamp(3));
            Assert.True(rank == default || Compare(rank, next) < 0);
            rank = next;
        }
    }
}

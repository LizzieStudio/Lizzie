using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class RowRankTests
{
    private static int Compare(string a, string b) => RowRank.Comparer.Compare(a, b);

    // A stamp from the hybrid clock, which never repeats.
    private static long _clock = 1;

    private static SnowportId Stamp(byte source = 1) => new(_clock++, source);

    [Fact]
    public void NewRankIsBetweenItsNeighbors()
    {
        // Inserts at random places, the way rows are inserted above and below each other.
        var random = new Random(7);
        var ranks = new List<string> { RowRank.New(null, null, Stamp()) };
        for (int i = 0; i < 2000; i++)
        {
            int at = random.Next(ranks.Count + 1);
            var prev = at > 0 ? ranks[at - 1] : null;
            var next = at < ranks.Count ? ranks[at] : null;
            ranks.Insert(
                at,
                RowRank.New(prev, next, Stamp((byte)random.Next(Snowport.SourceCount)))
            );
        }

        for (int i = 1; i < ranks.Count; i++)
            Assert.True(Compare(ranks[i - 1], ranks[i]) < 0, $"{ranks[i - 1]} !< {ranks[i]}");
    }

    [Fact]
    public void PlayersInsertingInTheSamePlaceGetDifferentRanks()
    {
        var prev = RowRank.New(null, null, Stamp());
        var next = RowRank.New(prev, null, Stamp());
        long at = _clock++;

        var ranks = Enumerable
            .Range(0, Snowport.SourceCount)
            .Select(s => RowRank.New(prev, next, new SnowportId(at, (byte)s)))
            .ToList();

        Assert.Equal(ranks.Count, ranks.Distinct().Count());
        Assert.All(ranks, r => Assert.True(Compare(prev, r) < 0 && Compare(r, next) < 0));
    }

    [Fact]
    public void RanksMadeInTheSamePlaceAtDifferentTimesDiffer()
    {
        // A row whose creation is undone, then another inserted where it was, then the first redone.
        var prev = RowRank.New(null, null, Stamp());
        var next = RowRank.New(prev, null, Stamp());

        var undone = RowRank.New(prev, next, Stamp());
        var inserted = RowRank.New(prev, next, Stamp());

        Assert.NotEqual(undone, inserted);
    }

    [Fact]
    public void ARankFitsBetweenRanksMadeInTheSamePlace()
    {
        var prev = RowRank.New(null, null, Stamp());
        var next = RowRank.New(prev, null, Stamp());
        var ranks = Enumerable
            .Range(0, 100)
            .Select(_ => RowRank.New(prev, next, Stamp((byte)(_clock % 64))))
            .Order(RowRank.Comparer)
            .ToList();

        for (int i = 1; i < ranks.Count; i++)
        {
            var between = RowRank.New(ranks[i - 1], ranks[i], Stamp());
            Assert.True(Compare(ranks[i - 1], between) < 0 && Compare(between, ranks[i]) < 0);
        }
    }

    [Fact]
    public void RankFitsAfterARankItPrefixes()
    {
        // The admin source's stamps start with the lowest stamp character, which leaves room below the next rank.
        var a = RowRank.New(null, null, Stamp(0));
        var b = RowRank.New(a, null, Stamp(5));
        var c = RowRank.New(a, b, Stamp(0));
        var d = RowRank.New(c, b, Stamp(0));
        var e = RowRank.New(a, c, Stamp(63));

        Assert.True(Compare(a, e) < 0 && Compare(e, c) < 0);
        Assert.True(Compare(c, d) < 0 && Compare(d, b) < 0);
    }

    [Fact]
    public void AppendedRanksKeepIncreasing()
    {
        string rank = null;
        for (int i = 0; i < 1000; i++)
        {
            var next = RowRank.New(rank, null, Stamp(3));
            Assert.True(rank == null || Compare(rank, next) < 0);
            rank = next;
        }
    }
}

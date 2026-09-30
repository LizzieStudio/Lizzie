using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class RowRankTests
{
    private static int Compare(string a, string b) => RowRank.Comparer.Compare(a, b);

    [Fact]
    public void NewRankIsBetweenItsNeighbours()
    {
        // Inserts at random places, the way rows are inserted above and below each other.
        var random = new Random(7);
        var ranks = new List<string> { RowRank.New(null, null, 0) };
        for (int i = 0; i < 2000; i++)
        {
            int at = random.Next(ranks.Count + 1);
            var prev = at > 0 ? ranks[at - 1] : null;
            var next = at < ranks.Count ? ranks[at] : null;
            ranks.Insert(at, RowRank.New(prev, next, (byte)random.Next(Snowport.SourceCount)));
        }

        for (int i = 1; i < ranks.Count; i++)
            Assert.True(Compare(ranks[i - 1], ranks[i]) < 0, $"{ranks[i - 1]} !< {ranks[i]}");
    }

    [Fact]
    public void PlayersInsertingInTheSamePlaceGetDifferentRanks()
    {
        var prev = RowRank.New(null, null, 0);
        var next = RowRank.New(prev, null, 0);

        var ranks = Enumerable
            .Range(0, Snowport.SourceCount)
            .Select(s => RowRank.New(prev, next, (byte)s))
            .ToList();

        Assert.Equal(ranks.Count, ranks.Distinct().Count());
        Assert.All(ranks, r => Assert.True(Compare(prev, r) < 0 && Compare(r, next) < 0));
    }

    [Fact]
    public void RankFitsAfterARankItPrefixes()
    {
        // The host's rank ends with the lowest source character, which leaves room below the next rank.
        var a = RowRank.New(null, null, 0);
        var b = RowRank.New(a, null, 5);
        var c = RowRank.New(a, b, 0);
        var d = RowRank.New(c, b, 0);
        var e = RowRank.New(a, c, 63);

        Assert.True(Compare(a, e) < 0 && Compare(e, c) < 0);
        Assert.True(Compare(c, d) < 0 && Compare(d, b) < 0);
    }

    [Fact]
    public void AppendedRanksKeepIncreasing()
    {
        string rank = null;
        for (int i = 0; i < 1000; i++)
        {
            var next = RowRank.New(rank, null, 3);
            Assert.True(rank == null || Compare(rank, next) < 0);
            rank = next;
        }
    }
}

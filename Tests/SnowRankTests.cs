using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Lizzie.Tests;

[Collection(ClockSwaps.Name)]
public class SnowRankTests
{
    private sealed record Item(SnowRank Rank) : IRanked;

    private static List<IRanked> Sorted(IEnumerable<IRanked> items) =>
        items.OrderBy(i => i.Rank).ToList();

    private static string Saved(SnowRank rank) =>
        JsonSerializer.Serialize(rank, LizzieJson.EventOptions);

    // Items with ranks made at the end, one at a time.
    private static List<IRanked> Appended(int count)
    {
        var items = new List<IRanked>();
        for (int i = 0; i < count; i++)
            items.Add(new Item(SnowRank.At(items, items.Count)));
        return items;
    }

    [Fact]
    public void NewRanksGoAtTheirIndex()
    {
        // Inserts, batches, deletes and restores at random places, the way rows are edited and undone.
        var random = new Random(7);
        var items = new List<IRanked>();
        var hidden = new List<IRanked>();
        for (int n = 0; n < 3000; n++)
        {
            var shown = Sorted(items.Except(hidden));
            switch (random.Next(4))
            {
                case 0 when shown.Count > 0:
                    hidden.Add(shown[random.Next(shown.Count)]);
                    break;
                case 1 when hidden.Count > 0:
                    hidden.RemoveAt(random.Next(hidden.Count));
                    break;
                default:
                    int at = random.Next(shown.Count + 1);
                    var batch = SnowRank
                        .At(shown, at, random.Next(1, 6))
                        .Select(r => (IRanked)new Item(r))
                        .ToList();
                    items.AddRange(batch);
                    var after = Sorted(items.Except(hidden));
                    Assert.True(
                        after.Skip(at).Take(batch.Count).SequenceEqual(batch),
                        $"""
                        {batch.Count} ranks made at {at} sorted to {string.Join(
                            ", ",
                            batch.Select(i => after.IndexOf(i))
                        )}.
                        Before: {(at > 0 ? shown[at - 1].Rank.ToString() : "none")}
                        After: {(at < shown.Count ? shown[at].Rank.ToString() : "none")}
                        Made: {string.Join(", ", batch.Select(i => i.Rank))}
                        """
                    );
                    break;
            }
        }
        Assert.Equal(items.Count, items.Select(i => i.Rank).Distinct().Count());
    }

    [Fact]
    public void ARankFitsBetweenARankAndOneThatExtendsIt()
    {
        // The middle of a batch is deleted, a row goes where it was, and the deletion is undone.
        var shown = SnowRank.AfterAll(4).Select(r => (IRanked)new Item(r)).ToList();
        var restored = shown[2];
        shown.RemoveAt(2);
        var inserted = new Item(SnowRank.At(shown, 2));
        shown = Sorted([.. shown, restored, inserted]);

        var between = new Item(SnowRank.At(shown, 3));

        Assert.True(
            Sorted([.. shown, between]).IndexOf(between) == 3,
            $"A rank made between {restored.Rank} and {inserted.Rank} sorted outside them: {between.Rank}"
        );
    }

    [Fact]
    public void PlayersInsertingInTheSamePlaceGetDifferentRanks()
    {
        var items = Appended(2);
        var local = Snowport.Clock;
        try
        {
            // Every player's clock is where the local one is, as if they all inserted at once.
            var ranks = Enumerable
                .Range(0, Snowport.SourceCount)
                .Select(source =>
                {
                    Snowport.Clock = local.WithSource((byte)source);
                    return SnowRank.At(items, 1);
                })
                .ToList();

            Assert.Equal(ranks.Count, ranks.Distinct().Count());
            Assert.All(
                ranks,
                r => Assert.True(items[0].Rank.CompareTo(r) < 0 && r.CompareTo(items[1].Rank) < 0)
            );
        }
        finally
        {
            Snowport.Clock = local;
        }
    }

    [Fact]
    public void RanksMadeInTheSamePlaceAtDifferentTimesDiffer()
    {
        // A row whose creation is undone, then another inserted where it was, then the first redone.
        var items = Appended(2);

        var undone = SnowRank.At(items, 1);
        var inserted = SnowRank.At(items, 1);

        Assert.NotEqual(undone, inserted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AtPutsANewRankAtItsIndex(int index)
    {
        var items = Appended(3);

        items.Insert(index, new Item(SnowRank.At(items, index)));

        var sorted = Sorted(items);
        Assert.True(
            sorted.SequenceEqual(items),
            $"A rank made at {index} sorted to {sorted.IndexOf(items[index])}."
        );
    }

    [Fact]
    public void EndsAndStartsStayShort()
    {
        var items = new List<IRanked>();
        for (int i = 0; i < 1000; i++)
        {
            int at = i % 2 == 0 ? items.Count : 0;
            var item = new Item(SnowRank.At(items, at));
            items.Insert(at, item);
            var saved = Saved(item.Rank);
            var single = Saved(SnowRank.AfterAll(1)[0]);
            Assert.True(
                saved.Length <= single.Length,
                $"Rank {i} saved as {saved}, longer than a single new end, {single}."
            );
        }
        Assert.True(
            Sorted(items).SequenceEqual(items),
            "Ranks made at the ends sorted out of place."
        );
    }

    [Fact]
    public void InsertsInOnePlaceStayShort()
    {
        // Rows typed in one after another, and rows each inserted above the last.
        const int inserts = 1000;
        var below = Appended(2);
        var above = Appended(2);
        for (int i = 0; i < inserts; i++)
        {
            below.Insert(i + 1, new Item(SnowRank.At(below, i + 1)));
            above.Insert(1, new Item(SnowRank.At(above, 1)));
        }

        Assert.True(
            Sorted(below).SequenceEqual(below),
            "Rows typed in one after another sorted out of place."
        );
        Assert.True(
            Sorted(above).SequenceEqual(above),
            "Rows inserted above the last sorted out of place."
        );
        var longest = below.Concat(above).Select(i => Saved(i.Rank)).MaxBy(s => s.Length);
        Assert.True(
            longest.Length < inserts / 2,
            $"After {inserts} inserts in one place, a rank saved in {longest.Length} characters, 1 or more for every 2 inserts: {longest}"
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RanksMadeTogetherKeepTheirOrder(bool end)
    {
        // Enough that their places take one, two and three digits.
        var ranks = end ? SnowRank.AfterAll(4097) : SnowRank.BeforeAll(4097);

        int back = ranks
            .Zip(ranks.Skip(1))
            .ToList()
            .FindIndex(p => p.First.CompareTo(p.Second) >= 0);
        if (back >= 0)
            Assert.Fail(
                $"Rank {back + 1}, {ranks[back + 1]}, sorted before rank {back}, {ranks[back]}."
            );
    }

    [Fact]
    public void TheDefaultRankSortsFirst()
    {
        var items = Appended(1);
        var start = SnowRank.At(items, 0);

        Assert.True(default(SnowRank).CompareTo(start) < 0 && start.CompareTo(items[0].Rank) < 0);
    }

    [Fact]
    public void ARankLoadsAsSaved()
    {
        var items = Appended(2);
        var rank = SnowRank.At(items, 1, 4)[3];

        var json = JsonSerializer.Serialize(rank, LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<SnowRank>(json, LizzieJson.EventOptions);

        Assert.True(back == rank, $"{rank} saved as {json} and loaded as {back}.");
    }
}

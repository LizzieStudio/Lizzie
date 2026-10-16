using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Lizzie.Replication.Machinery;
using Xunit;

public class SaveCompactionTests
{
    private static SnowTag Tag(int counter, byte source = Snowport.HostSource) =>
        new(source, counter);

    private static SnowportId Id(long clock, byte source = Snowport.HostSource) =>
        new(clock, source);

    private static ComponentState Component(int id) => new() { Id = Tag(id) };

    private static Prototype DeletedPrototype(int id) => new() { Id = Tag(id), Deleted = true };

    private static TableEvent Save(params Replicated[] records) =>
        new() { Id = Id(1_000_000, Snowport.AdminSource), Records = records };

    private static TableEvent Load(string json) =>
        JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.EventOptions);

    [StackTraceHidden]
    private static void AssertKept(IEnumerable<Replicated> atoms, params int[] expected)
    {
        var kept = SaveCompaction.Collect(atoms).Select(a => a.Id).OrderBy(t => t).ToArray();
        var want = expected.Select(n => Tag(n)).OrderBy(t => t).ToArray();
        Assert.True(
            kept.SequenceEqual(want),
            $"Kept the wrong records.\n\nExpected: {string.Join(", ", want)}\nKept:     {string.Join(", ", kept)}"
        );
    }

    [Fact]
    public void ListedRecordsAreKept() => AssertKept([Component(1), Component(2)], 1, 2);

    [Fact]
    public void AnUnreferencedDeletedPrototypeIsDropped() =>
        AssertKept([Component(1), DeletedPrototype(2)], 1);

    [Fact]
    public void ADeletedPrototypeAComponentUsesIsKept() =>
        AssertKept([Component(1) with { PrototypeRef = Tag(2) }, DeletedPrototype(2)], 1, 2);

    [Fact]
    public void ADataSetKeepsItsRows() =>
        AssertKept(
            [new DataSet { Id = Tag(1) }, new DataRow { Id = Tag(2), DataSetId = Tag(1) }],
            1,
            2
        );

    [Fact]
    public void RowsOfAMissingDataSetAreDropped() =>
        AssertKept([new DataRow { Id = Tag(2), DataSetId = Tag(1) }]);

    [Fact]
    public void TagsBecomeAdminTagsFromOneInOrder()
    {
        var json = SaveCompaction.Serialize(
            Save(
                Component(9) with
                {
                    ContainerRef = Tag(5, source: 3),
                },
                Component(7) with
                {
                    PrototypeRef = Tag(9),
                }
            )
        );

        var back = Load(json).Records.Cast<ComponentState>().ToArray();

        // in order: 7 < 9 < (source 3, 5)
        Assert.Equal(new SnowTag(2), back[0].Id);
        Assert.Equal(new SnowTag(3), back[0].ContainerRef);
        Assert.Equal(new SnowTag(1), back[1].Id);
        Assert.Equal(new SnowTag(2), back[1].PrototypeRef);
        Assert.All(back, c => Assert.Equal(Snowport.AdminSource, c.Id.source));
        Assert.Equal(SnowTag.Empty, back[1].ContainerRef);
    }

    [Fact]
    public void DictionaryKeysAreRenumbered()
    {
        var column = Tag(50);
        var json = SaveCompaction.Serialize(
            Save(
                new DataSet { Id = Tag(40), Columns = [new Column { Id = column, Name = "Name" }] },
                new DataRow
                {
                    Id = Tag(41),
                    DataSetId = Tag(40),
                    Data = ImmutableDictionary<SnowTag, string>.Empty.Add(column, "Ace"),
                }
            )
        );

        var back = Load(json).Records;
        var set = Assert.IsType<DataSet>(back[0]);
        var row = Assert.IsType<DataRow>(back[1]);
        Assert.Equal(set.Id, row.DataSetId);
        Assert.Equal("Ace", row.Data[set.Columns[0].Id]);
        Assert.Equal(new SnowTag(3), set.Columns[0].Id);
    }

    [Fact]
    public void SnowportIdsBecomeAdminIdsFromOneAndKeepStackingOrder()
    {
        var below = new ZOrder(ZTarget.Top, 0, Id(500, source: 2));
        var above = new ZOrder(ZTarget.Top, 0, Id(900, source: 1));
        var json = SaveCompaction.Serialize(
            Save(Component(1) with { ZOrder = above }, Component(2) with { ZOrder = below })
        );

        var back = Load(json);
        var components = back.Records.Cast<ComponentState>().ToArray();

        Assert.True(back.IsAdmin, "The save event should stay an admin event.");
        Assert.Equal(Id(1, Snowport.AdminSource), components[1].ZOrder.Stamp);
        Assert.Equal(Id(2, Snowport.AdminSource), components[0].ZOrder.Stamp);
        Assert.Equal(Id(3, Snowport.AdminSource), back.Id);
        Assert.True(components[0].ZOrder > components[1].ZOrder, "Stacking order changed.");
    }

    [Fact]
    public void UnsetIdsStayUnset()
    {
        var json = SaveCompaction.Serialize(Save(Component(1)));
        var back = (ComponentState)Load(json).Records[0];

        Assert.Equal(SnowTag.Empty, back.PrototypeRef);
        Assert.Equal(SnowportId.Empty, back.ZOrder.Stamp);
    }

    [Fact]
    public void SavingALoadedSaveChangesNothing()
    {
        var first = SaveCompaction.Serialize(
            Save(
                Component(8) with
                {
                    PrototypeRef = Tag(3, source: 2),
                    ZOrder = new ZOrder(ZTarget.Bottom, 1, Id(77)),
                },
                new Prototype { Id = Tag(3, source: 2), Name = "Card" }
            )
        );

        var second = SaveCompaction.Serialize(Load(first));

        Assert.Equal(first, second);
    }
}

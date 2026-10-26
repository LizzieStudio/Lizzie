using System.Linq;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A stage exists only while its parents lead up to a root,
/// and merging stages takes the lower stage's writes.
/// </summary>
public class StageTests
{
    private static Ref<Stage> Tag(int counter) => new(new SnowTag(Snowport.HostSource, counter));

    private static Stage Blueprint(int id, int parent = 0, bool deleted = false) =>
        new()
        {
            Id = Tag(id),
            ParentRef = parent == 0 ? Ref<Stage>.Empty : Tag(parent),
            Deleted = deleted,
        };

    private static Stage Table(int id, int parent = 0) =>
        Blueprint(id, parent) with
        {
            IsTable = true,
        };

    // A root blueprint with two children, the current table on the first and nothing on the second.
    private static ListReader Project(params Atom[] more) =>
        new([
            Blueprint(1),
            Blueprint(2, parent: 1),
            Blueprint(3, parent: 1),
            Table(9, parent: 2),
            new CurrentTable { StageRef = Tag(9) },
            .. more,
        ]);

    private static ComponentPosition Position(int id, int stage, int x) =>
        new()
        {
            Id = Tag(id).Tag,
            StageRef = Tag(stage),
            X = x,
        };

    [Fact]
    public void TheCurrentTablesChainHoldsItsBlueprintsButNotTheirSiblings()
    {
        var R = Project();
        Assert.True(Stage.IsCurrentTableOrAncestor(R, Tag(1)));
        Assert.True(Stage.IsCurrentTableOrAncestor(R, Tag(2)));
        Assert.False(Stage.IsCurrentTableOrAncestor(R, Tag(3)));
    }

    [Fact]
    public void TablesAreListedLikeBlueprintsWhetherOrNotTheyreCurrent()
    {
        var R = Project(Table(4), Table(5, parent: 4) with { Deleted = true });
        Assert.Equal([Tag(1), Tag(4)], Stage.GetChildren(R, Ref<Stage>.Empty).Select(s => s.Id));
        Assert.Equal([Tag(9)], Stage.GetChildren(R, Tag(2)).Select(s => s.Id));
        Assert.Empty(Stage.GetChildren(R, Tag(4)));
    }

    [Fact]
    public void ANewTableIsNumberedPastTheHighestInTheTree()
    {
        Assert.Equal("Table 1", Stage.NextTableName(Project()));
        var R = Project(
            Table(4) with
            {
                Name = "Table 2",
            },
            Blueprint(5, parent: 3) with
            {
                Name = "Table 7",
            },
            Table(6, parent: 5) with
            {
                Name = "Table 9",
                Deleted = true,
            },
            Table(7, parent: 2) with
            {
                Name = "Table 3b",
            }
        );
        Assert.Equal("Table 8", Stage.NextTableName(R));
    }

    [Fact]
    public void AUnionIntoTheParentTakesTheChildsWrites()
    {
        var R = Project(
            Position(10, stage: 1, x: 1),
            Position(10, stage: 2, x: 2),
            Position(11, stage: 1, x: 3)
        );
        Assert.Equal(
            [Position(10, stage: 1, x: 2)],
            Stage.Union(R, Tag(1), Tag(1), Tag(2)).ToArray()
        );
    }

    [Fact]
    public void AUnionIntoAChildKeepsItsOwnWrites()
    {
        var R = Project(
            Position(10, stage: 1, x: 1),
            Position(10, stage: 2, x: 2),
            Position(11, stage: 1, x: 3)
        );
        Assert.Equal(
            [Position(11, stage: 2, x: 3)],
            Stage.Union(R, Tag(2), Tag(1), Tag(2)).ToArray()
        );
    }

    [Fact]
    public void AUnionOfOneCopiesItsWrites()
    {
        var R = Project(Position(10, stage: 2, x: 2), Position(11, stage: 3, x: 3));
        Assert.Equal([Position(10, stage: 4, x: 2)], Stage.Union(R, Tag(4), Tag(2)).ToArray());
    }

    [Fact]
    public void ATablesChainRunsUpToItsRoot()
    {
        var R = Project();
        Assert.Equal([Tag(9), Tag(2), Tag(1)], Stage.Chain(R, Tag(9)).Select(s => s.Id).ToArray());
    }

    [Fact]
    public void ABlueprintUnderADeletedOneIsUnreachable()
    {
        var R = Project(Blueprint(4, parent: 3, deleted: true), Blueprint(5, parent: 4));
        Assert.False(Stage.Reachable(R, Tag(5)));
        Assert.Empty(Stage.Chain(R, Tag(5)));
    }

    [Fact]
    public void ALoopAndWhatsUnderItAreUnreachable()
    {
        var R = Project(Blueprint(4, parent: 5), Blueprint(5, parent: 4), Blueprint(6, parent: 4));
        Assert.False(Stage.Reachable(R, Tag(4)));
        Assert.False(Stage.Reachable(R, Tag(5)));
        Assert.False(Stage.Reachable(R, Tag(6)));
        Assert.Empty(Stage.Chain(R, Tag(6)));
    }
}

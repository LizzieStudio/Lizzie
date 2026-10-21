using System.Linq;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Blueprints can be deleted only when nothing builds on them,
/// and a stage exists only while its parents lead up to a root blueprint.
/// </summary>
public class StageTests
{
    private static SnowTag Tag(int counter) => new(Snowport.HostSource, counter);

    private static Stage Blueprint(int id, int parent = 0, bool deleted = false) =>
        new()
        {
            Id = Tag(id),
            ParentRef = parent == 0 ? SnowTag.Empty : Tag(parent),
            Deleted = deleted,
        };

    private static Stage Game(int id, int blueprint) =>
        new()
        {
            Id = Tag(id),
            ParentRef = Tag(blueprint),
            IsGame = true,
        };

    // A root blueprint with two children, the game on the first and nothing on the second.
    private static ListReader Project(params Atom[] more) =>
        new([
            Blueprint(1),
            Blueprint(2, parent: 1),
            Blueprint(3, parent: 1),
            Game(9, blueprint: 2),
            new CurrentGame { StageRef = Tag(9) },
            .. more,
        ]);

    [Fact]
    public void ABlueprintNothingBuildsOnIsALeaf()
    {
        var R = Project();
        Assert.True(Stage.IsLeaf(R, R.Get(Stage.ById, Tag(3))));
    }

    [Fact]
    public void ABlueprintWithABlueprintOnItIsNotALeaf()
    {
        var R = Project();
        Assert.False(Stage.IsLeaf(R, R.Get(Stage.ById, Tag(1))));
    }

    [Fact]
    public void ABlueprintWithOnlyDeletedBlueprintsOnItIsALeaf()
    {
        var R = Project(Blueprint(4, parent: 3, deleted: true));
        Assert.True(Stage.IsLeaf(R, R.Get(Stage.ById, Tag(3))));
    }

    [Fact]
    public void TheGamesBlueprintIsNotALeaf()
    {
        var R = Project();
        Assert.False(Stage.IsLeaf(R, R.Get(Stage.ById, Tag(2))));
    }

    [Fact]
    public void AGamesChainRunsUpToItsRoot()
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

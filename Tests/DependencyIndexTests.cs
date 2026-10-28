using System.Diagnostics;
using System.Linq;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A change finds the watchers that read what it touched, and only those.
/// </summary>
public class DependencyIndexTests
{
    private static readonly Lookup<ComponentPosition, SnowTag> ByContainer = new(s =>
        s.Container is InComponent c ? c.Component.Tag : SnowTag.Empty
    );

    private readonly DependencyIndex<string> _index = new();

    private static SnowTag Tag(int counter) => new(Snowport.HostSource, counter);

    [StackTraceHidden]
    private void AssertAffected(Touch[] touches, params string[] expected)
    {
        var affected = _index.Affected(touches).Order().ToArray();
        Assert.True(
            affected.SequenceEqual(expected.Order()),
            $"The wrong watchers were affected.\n\nExpected: {string.Join(", ", expected.Order())}\nAffected: {string.Join(", ", affected)}"
        );
    }

    [Fact]
    public void AChangeAffectsOnlyTheWatchersOfTheValuesItTouched()
    {
        _index.Add("reads 1", ComponentPosition.ById, Tag(1));
        _index.Add("reads 2", ComponentPosition.ById, Tag(2));
        _index.Add("reads container 5", ByContainer, Tag(5));
        _index.Add("reads container 6", ByContainer, Tag(6));

        AssertAffected(
            [
                new Touch(ComponentPosition.ById, Tag(1), false),
                new Touch(ByContainer, Tag(5), false),
            ],
            "reads 1",
            "reads container 5"
        );
    }

    [Fact]
    public void ACountWatcherIsAffectedOnlyWhenTheCountChanges()
    {
        _index.Add("counts container 5", ByContainer, Tag(5), count: true);

        AssertAffected([new Touch(ByContainer, Tag(5), false)]);
        AssertAffected([new Touch(ByContainer, Tag(5), true)], "counts container 5");
    }

    [Fact]
    public void AClearedWatcherIsNoLongerAffected()
    {
        _index.Add("reads 1", ComponentPosition.ById, Tag(1));
        _index.Clear("reads 1");

        AssertAffected([new Touch(ComponentPosition.ById, Tag(1), false)]);
    }
}

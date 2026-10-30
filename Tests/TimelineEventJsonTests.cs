using System.Text.Json;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

public class TimelineEventJsonTests
{
    private static SnowportId Id(long clock, byte source) => new(clock, source);

    private static TimelineEvent RoundTrip(TimelineEvent e, out string json)
    {
        json = JsonSerializer.Serialize(e, LizzieJson.EventOptions);
        return JsonSerializer.Deserialize<TimelineEvent>(json, LizzieJson.EventOptions);
    }

    [Fact]
    public void CommandIsWrittenAsItsId()
    {
        var e = new TimelineEvent { Id = Id(5, 1), Command = new CommandName("component.flip") };

        var back = RoundTrip(e, out var json);

        Assert.Contains("\"command\":\"component.flip\"", json);
        Assert.Equal(new CommandName("component.flip"), back.Command);
    }

    [Fact]
    public void TransitionLeavesCommandOut()
    {
        var e = new TimelineEvent { Id = Id(5, 1) };

        var back = RoundTrip(e, out var json);

        Assert.DoesNotContain("\"command\"", json);
        Assert.Null(back.Command);
    }

    [Fact]
    public void ReversalKeepsItsCommand()
    {
        var e = new TimelineEvent
        {
            Id = Id(5, 1),
            Command = new CommandName("app.undo_others"),
            Undo = new UndoFlag { Reverses = Id(3, 2) },
        };

        var back = RoundTrip(e, out _);

        Assert.Equal(new CommandName("app.undo_others"), back.Command);
        Assert.Equal(Id(3, 2), back.Undo.Reverses);
    }

    [Fact]
    public void UnsetKeepsItsQueryAndKey()
    {
        var position = new StageKey<ComponentPosition>(
            new Ref<Stage>(new SnowTag(1, 4)),
            new Ref<Component>(new SnowTag(1, 5))
        );
        var dataset = new Ref<Dataset>(new SnowTag(2, 6));
        var e = new TimelineEvent
        {
            Id = Id(5, 1),
            Unsets =
            [
                new Unset(ComponentPosition.ById, position),
                new Unset(DataColumn.ByDataset, dataset),
            ],
        };

        var back = RoundTrip(e, out var json);

        Assert.Contains("{\"type\":\"DataColumn\",\"query\":\"ByDataset\",\"key\":", json);
        Assert.Equal(e.Unsets, back.Unsets);
    }
}

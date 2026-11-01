using System.Collections.Immutable;
using System.Text.Json;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

public class TimelineEventJsonTests
{
    private static SnowStamp Id(long clock, int source) => new(clock, new Source(source));

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
    public void AnEndKeepsItsTransitionAndADraftItsStamps()
    {
        var outer = new Transition(Id(2, 1));
        var inner = new Transition(Id(3, 1));
        var e = new TimelineEvent
        {
            Id = Id(5, 1),
            Ends = inner,
            Writes = [new Prototype { Id = new(1), Transitions = [inner, outer] }],
        };

        var back = RoundTrip(e, out var json);

        Assert.Contains($"\"ends\":{(long)inner.Start}", json);
        Assert.Contains($"\"transitions\":[{(long)outer.Start},{(long)inner.Start}]", json);
        Assert.Equal(inner, back.Ends);
        Assert.Equal([outer, inner], back.Writes[0].Transitions);
    }

    [Fact]
    public void ACommittedWriteLeavesItsTransitionsOut()
    {
        var e = new TimelineEvent
        {
            Id = Id(5, 1),
            Writes = [new Prototype { Id = new(1), Transitions = [] }],
        };

        var back = RoundTrip(e, out var json);

        Assert.DoesNotContain("\"transitions\"", json);
        Assert.True(back.Writes[0].Transitions.IsEmpty);
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
            new Ref<Stage>(new SnowTag(Source.Host, 4)),
            new Ref<Component>(new SnowTag(Source.Host, 5))
        );
        var dataset = new Ref<Dataset>(new SnowTag(new Source(2), 6));
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

    [Fact]
    public void SourcesAreWrittenAsNumbers()
    {
        var player = new Source(3);
        var seat = new Ref<Seat>(new SnowTag(Source.Host, 7));
        var e = new TimelineEvent
        {
            Id = Id(5, 1),
            Writes =
            [
                new Seating
                {
                    Seats = ImmutableDictionary<Source, Ref<Seat>>.Empty.Add(player, seat),
                },
                new Selection { Id = new(8), Player = player },
            ],
            Unsets = [new Unset(Selection.ByPlayer, player)],
        };

        var back = RoundTrip(e, out var json);

        Assert.Contains("\"seats\":{\"3\":", json);
        Assert.Contains("\"player\":3", json);
        Assert.Equal(seat, Assert.IsType<Seating>(back.Writes[0]).Seats[player]);
        Assert.Equal(player, Assert.IsType<Selection>(back.Writes[1]).Player);
        Assert.Equal(e.Unsets, back.Unsets);
    }
}

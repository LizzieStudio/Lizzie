using System.Text.Json;
using Xunit;

public class TableEventJsonTests
{
    private static SnowportId Id(ulong clock, byte source) => new((clock << 6) | source);

    private static TableEvent RoundTrip(TableEvent e, out string json)
    {
        json = JsonSerializer.Serialize(e, LizzieJson.EventOptions);
        return JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.EventOptions);
    }

    [Fact]
    public void CommandIsWrittenAsItsId()
    {
        var e = new TableEvent { Id = Id(5, 1), Command = new CommandName("component.flip") };

        var back = RoundTrip(e, out var json);

        Assert.Contains("\"a\":\"component.flip\"", json);
        Assert.Equal(new CommandName("component.flip"), back.Command);
    }

    [Fact]
    public void GestureLeavesCommandOut()
    {
        var e = new TableEvent { Id = Id(5, 1) };

        var back = RoundTrip(e, out var json);

        Assert.DoesNotContain("\"a\"", json);
        Assert.Null(back.Command);
    }

    [Fact]
    public void ReversalKeepsItsCommand()
    {
        var e = new TableEvent
        {
            Id = Id(5, 1),
            Command = new CommandName("app.undo_others"),
            Undo = new UndoFlag { Reverses = Id(3, 2) },
        };

        var back = RoundTrip(e, out _);

        Assert.Equal(new CommandName("app.undo_others"), back.Command);
        Assert.Equal(Id(3, 2), back.Undo.Reverses);
    }
}

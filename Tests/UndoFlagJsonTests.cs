using System.Text.Json;
using Lizzie.Replication.Machinery;
using Xunit;

/// <summary>Reversals as they're saved and sent, with the game's JSON options.</summary>
public class UndoFlagJsonTests
{
    private static SnowportId Id(long clock, byte source) => new(clock, source);

    [Fact]
    public void RetiredReversalsSurviveTheRoundTrip()
    {
        var reversal = new TableEvent
        {
            Id = Id(5, 1),
            Undo = new UndoFlag
            {
                Reverses = Id(3, 1),
                ByRedo = true,
                Also = [Id(4, 2)],
            },
        };

        var json = JsonSerializer.Serialize(reversal, LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.EventOptions);

        Assert.Equal(reversal.Undo.Reverses, back.Undo.Reverses);
        Assert.True(back.Undo.ByRedo);
        Assert.Equal(reversal.Undo.Also, back.Undo.Also);
    }

    [Fact]
    public void ReversalThatRetiresNothingLeavesAlsoOut()
    {
        var reversal = new TableEvent
        {
            Id = Id(5, 1),
            Undo = new UndoFlag { Reverses = Id(3, 1) },
        };

        var json = JsonSerializer.Serialize(reversal, LizzieJson.EventOptions);
        var back = JsonSerializer.Deserialize<TableEvent>(json, LizzieJson.EventOptions);

        Assert.DoesNotContain("\"also\"", json);
        Assert.Null(back.Undo.Also);
    }
}

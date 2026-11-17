using System.Linq;
using System.Text.Json;
using GdUnit4;
using Godot;
using Lizzie.Replication.Machinery;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class QuickSuitSaveTests
{
    // Saving writes the suits' colors, which needs the engine.
    // A deck has one suit left, A. A card on the table still shows the removed suit B,
    // and nothing shows the removed suit C.
    [TestCase]
    [RequireGodotRuntime]
    public void ASaveKeepsARemovedSuitOnlyWhileACardShowsIt()
    {
        SnowTag a = new(10),
            b = new(11),
            c = new(12);
        var side = new QuickSide().Add(a, new QuickSuit { Values = "1-3" });
        side = side with
        {
            Suits = side
                .Suits.With(b, new QuickSuit { Values = "shown-suit", Background = Colors.Red })
                .With(c, new QuickSuit { Values = "unshown-suit" }),
        };
        var atoms = new Atom[]
        {
            new Stage { Id = new(new(1)) },
            new Prototype
            {
                Id = new(new(2)),
                Parameters = new PrintedParameters { Front = side },
            },
            new Component
            {
                Id = new(new(3)),
                PrototypeRef = new(new(2)),
                Item = new PrintedItemRef(new QuickFace(b, "7"), null),
            },
            new ComponentPosition { Id = new(new(3)), StageRef = new(new(1)) },
        };

        var json = SaveCompaction.Serialize(
            SaveCompaction.Event(
                atoms.Select(atom => new AtomEntry(atom, new SnowStamp(1, Source.Host)))
            )
        );
        var loaded = JsonSerializer.Deserialize<TimelineEvent>(json, LizzieJson.EventOptions);
        var card = loaded.Writes.OfType<Component>().Single();
        var front = (QuickSide)
            ((PrintedParameters)loaded.Writes.OfType<Prototype>().Single().Parameters).Front;
        var shown = ((QuickFace)((PrintedItemRef)card.Item).Front).SuitRef;

        AssertThat(front.Suits.TryGetValue(shown, out var suit) && suit.Background == Colors.Red)
            .OverrideFailureMessage(
                $"The card's removed suit lost its look in the save, which is:\n{json}"
            )
            .IsTrue();
        AssertThat(json.Contains("unshown-suit"))
            .OverrideFailureMessage($"The save kept a removed suit no card shows:\n{json}")
            .IsFalse();
    }
}

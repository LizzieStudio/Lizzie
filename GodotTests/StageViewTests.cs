using System.Threading.Tasks;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class StageViewTests
{
    // Updating a deck rewrites what a card is, for every stage at once, without moving it.
    [TestCase]
    [RequireGodotRuntime]
    public async Task ACardShowsWhatItBecomes()
    {
        var stage = Ref<Stage>.Create();
        var prototype = new Prototype
        {
            Id = Ref<Prototype>.Create(),
            Parameters = new PrintedParameters(),
        };
        var card = new Component
        {
            Id = Ref<Component>.Create(),
            PrototypeRef = prototype.Id,
            Item = new PrintedItemRef(new FrameFace(1), null),
        };
        Atoms.R.WriteAdmin(
            new Stage { Id = stage },
            prototype,
            card,
            new ComponentPosition { Id = card.Id, StageRef = stage }
        );
        var view = new StageView { StageRef = stage };
        using var runner = ISceneRunner.Load(view, true);
        await runner.AwaitIdleFrame();

        var becomes = new PrintedItemRef(new FrameFace(2), null);
        Atoms.R.WriteAdmin(card with { Item = becomes });
        await runner.AwaitIdleFrame();

        var shown = view.GetComponent(card.Id)?.Item;
        AssertThat(Equals(shown, becomes))
            .OverrideFailureMessage(
                $"The card shows {shown?.ToString() ?? "nothing"}, expected {becomes}."
            )
            .IsTrue();
    }
}

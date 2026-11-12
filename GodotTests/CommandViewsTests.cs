using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class CommandViewsTests
{
    // Only windows separate views, so a node a view shows through a SubViewport is still in that view.
    [TestCase]
    [RequireGodotRuntime]
    public void ANodeInASubViewportBelongsToTheViewAroundIt()
    {
        var view = GD.Load<PackedScene>("res://Scenes/Project/SeatsWindow.tscn")
            .Instantiate<SeatsWindow>();
        using var runner = ISceneRunner.Load(view, true);
        var container = new SubViewportContainer();
        view.AddChild(container);
        var subViewport = new SubViewport();
        container.AddChild(subViewport);
        var inside = new Node();
        subViewport.AddChild(inside);

        var found = CommandViews.Of(inside);
        AssertThat(found == view)
            .OverrideFailureMessage(
                $"A node in a SubViewport in the Seats window belongs to {found?.GetType().Name ?? "no view"}."
            )
            .IsTrue();
    }
}

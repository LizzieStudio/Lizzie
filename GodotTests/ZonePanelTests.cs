using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class ZonePanelTests
{
    // The panel has no row for a removed seat, but saving the zone keeps its rule,
    // so undoing the removal brings the rule back.
    [TestCase]
    [RequireGodotRuntime]
    public async Task SavingAZoneKeepsARemovedSeatsRule()
    {
        var removed = new Seat { Id = Ref<Seat>.Create(), Deleted = true };
        Atoms.R.WriteAdmin(removed);
        var panel = GD.Load<PackedScene>("res://Scenes/ComponentPanels/zone_panel.tscn")
            .Instantiate<ZonePanelDialogResult>();
        using var runner = ISceneRunner.Load(panel, true);
        panel.DisplayPrototype(
            new Prototype
            {
                Id = Ref<Prototype>.Create(),
                Parameters = new ZoneParameters { ExcludedSeats = [removed.Id] },
            }
        );
        await runner.AwaitIdleFrame();

        var saved = (ZoneParameters)panel.GetParams();
        AssertThat(saved.ExcludedSeats.Contains(removed.Id))
            .OverrideFailureMessage(
                $"Saving the zone dropped the removed seat's rule: blocked seats are [{string.Join(", ", saved.ExcludedSeats)}]."
            )
            .IsTrue();
    }
}

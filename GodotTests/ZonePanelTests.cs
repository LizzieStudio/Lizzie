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
                Parameters = new ZoneParameters
                {
                    Seats = WeakMap<Ref<Seat>, ZoneAccess>.Empty.With(
                        removed.Id,
                        ZoneAccess.Blocked
                    ),
                },
            }
        );
        await runner.AwaitIdleFrame();

        var saved = ((ZoneParameters)panel.GetParams()).Seats.GetValueOrDefault(removed.Id);
        AssertThat(saved == ZoneAccess.Blocked)
            .OverrideFailureMessage(
                $"Saving the zone changed the removed seat's rule to {saved}, expected Blocked."
            )
            .IsTrue();
    }
}

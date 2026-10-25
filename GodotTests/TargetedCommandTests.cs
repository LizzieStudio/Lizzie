using GdUnit4;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class TargetedCommandTests
{
    // A menu keeps the targets it opened with, so some may be gone when an item is picked.
    [TestCase]
    [RequireGodotRuntime]
    public void ACommandLeftWithTooFewTargetsDoesNotRun()
    {
        var row = new DataRow { Id = SnowTag.Create() };
        AtomService.Instance.WriteAdmin(row);
        AssertThat(AtomService.Instance.Get(DataRow.ById, row.Id))
            .OverrideFailureMessage("The row written for the test isn't in the atom service.")
            .IsNotNull();

        bool ran = false;
        var command = new AtomCommand<DataRow>
        {
            MinTargets = 2,
            SideEffects = (_, _) => ran = true,
        };
        command.Run([DataRow.ById.Ref(row.Id), DataRow.ById.Ref(SnowTag.Create())], null);
        AssertThat(ran)
            .OverrideFailureMessage(
                "A command for at least 2 rows ran with 1, after the other target was gone."
            )
            .IsFalse();
    }
}

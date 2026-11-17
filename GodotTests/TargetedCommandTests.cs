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
        var row = new DataRow { Id = Ref<DataRow>.Create() };
        Atoms.R.WriteAdmin(row);
        AssertThat(Atoms.R.Get(DataRow.ById, row.Id))
            .OverrideFailureMessage("The row written for the test isn't in the atom service.")
            .IsNotNull();

        bool ran = false;
        var command = new AtomCommand<DataRow>
        {
            MinTargets = 2,
            Effects = (_, _) =>
            {
                ran = true;
                return null;
            },
        };
        command.Run([row.Id, Ref<DataRow>.Create()], null);
        AssertThat(ran)
            .OverrideFailureMessage(
                "A command for at least 2 rows ran with 1, after the other target was gone."
            )
            .IsFalse();
    }
}

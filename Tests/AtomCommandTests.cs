using Xunit;

namespace Lizzie.Tests;

public class AtomCommandTests
{
    // With no atom service, no target resolves, as when every target is gone since the menu opened.
    [Fact]
    public void ACommandWhoseTargetsAreAllGoneRunsNoSideEffects()
    {
        bool ran = false;
        var command = new AtomCommand<DataRow> { SideEffects = (_, _) => ran = true };
        command.Run([DataRow.ById.Ref(new SnowTag(5))], null);
        Assert.False(ran, "The side effects ran with no atoms, though the command had a target.");
    }
}

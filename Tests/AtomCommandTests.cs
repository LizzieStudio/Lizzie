using Xunit;

namespace Lizzie.Tests;

public class AtomCommandTests
{
    // With no atom service, no target resolves, as when every target is gone since the menu opened.
    [Fact]
    public void ACommandWhoseTargetsAreAllGoneRunsNoEffects()
    {
        bool ran = false;
        var command = new AtomCommand<DataRow>
        {
            Effects = (_, _) =>
            {
                ran = true;
                return null;
            },
        };
        command.Run([DataRow.ById.Ref(new SnowTag(5))], null);
        Assert.False(ran, "The effects ran with no atoms, though the command had a target.");
    }
}

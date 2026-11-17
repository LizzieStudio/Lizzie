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
        command.Run([new Ref<DataRow>(5)], null);
        Assert.False(ran, "The effects ran with no atoms, though the command had a target.");
    }

    // The broken rule explains it ahead of the count, which would ask for a selection that can't help.
    [Fact]
    public void AnUnavailableCommandGivesItsBrokenRuleAsTheReason()
    {
        var command = new AtomCommand<DataRow>
        {
            AvailableWhen = [("Hands are off.", _ => false)],
            Effects = (_, _) => null,
        };
        Assert.False(
            command.IsAvailable(null),
            "The command is available though its rule is broken."
        );
        Assert.Equal("Hands are off.", command.DisabledReason(null, 0, []));
    }
}

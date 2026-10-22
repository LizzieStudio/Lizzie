using GdUnit4;
using static GdUnit4.Assertions;

namespace Lizzie;

[TestSuite]
public class EngineTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void AutoloadsAreLoaded() => AssertThat(AtomService.Instance).IsNotNull();
}

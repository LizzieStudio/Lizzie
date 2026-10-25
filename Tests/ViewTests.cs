using System.Linq;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Every <see cref="ICommandView"/> needs a <see cref="ViewAttribute"/> of its own,
/// since places in it go by its name.
/// </summary>
public class ViewTests
{
    /// <summary>The same check the game runs when it makes its first place.</summary>
    [Fact]
    public void EveryViewHasItsOwnName()
    {
        var problems = ViewAttribute.Problems().ToList();
        if (problems.Count > 0)
            Assert.Fail(string.Join("\n\n", problems));
    }
}

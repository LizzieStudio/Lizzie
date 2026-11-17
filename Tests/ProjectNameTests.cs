using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A project's name is its folder's name, so it has to work as a folder name on every desktop system.
/// </summary>
public class ProjectNameTests
{
    [Theory]
    [InlineData("My Game")]
    [InlineData("Café Racers")]
    // The save's format goes after the folder's save name, never the project's name.
    [InlineData("Name.v5")]
    // Only the exact device names are reserved.
    [InlineData("CONSOLE")]
    [InlineData("COM10")]
    public void ANameThatWorksEverywhereIsFine(string name)
    {
        Assert.Null(ProjectName.Problem(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Game")]
    [InlineData("Game ")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a\"b")]
    [InlineData("a|b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a%b")]
    public void GodotsRulesStillApply(string name)
    {
        Assert.NotNull(ProjectName.Problem(name));
    }

    [Theory]
    // Windows drops the period, so it would land on "Game".
    [InlineData("Game.")]
    // These name the location itself and its parent.
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("tab\there")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("COM²")]
    // Reserved even with an extension.
    [InlineData("NUL.txt")]
    public void WindowsRulesGodotLeavesOutAreAdded(string name)
    {
        Assert.NotNull(ProjectName.Problem(name));
    }
}

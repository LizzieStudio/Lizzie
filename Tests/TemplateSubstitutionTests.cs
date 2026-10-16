using System.Diagnostics;
using System.Linq;
using Lizzie.Scripts.Templating;
using Xunit;

public class TemplateSubstitutionTests
{
    [Fact]
    public void SubstitutedValuesAreNotSubstitutedAgain() =>
        Check("See {Text}", "{Title}", Row(("Title", "See {Text}"), ("Text", "hello")));

    [Fact]
    public void WidthIsAnOrdinaryColumnName() =>
        Check("wide {Height}", "{Width} {Height}", Row(("Width", "wide")));

    [Fact]
    public void ColumnsMatchCase() =>
        Check("hello {text}", "{Text} {text}", Row(("Title", "x"), ("Text", "hello")));

    [Theory]
    [InlineData("{Typo} stays")]
    [InlineData("{} and { Title } and {Title")]
    public void UnknownKeysStay(string text) => Check(text, text, Row(("Title", "x")));

    [Fact]
    public void ColumnsNeedARow() => Check("{Title}", "{Title}", null);

    private static TextureContext Row(params (string Column, string Value)[] values) =>
        new() { Values = values.ToDictionary(v => v.Column, v => v.Value) };

    [StackTraceHidden]
    private static void Check(string expected, string text, TextureContext context)
    {
        context ??= new TextureContext();
        var actual = TemplateElement.Substitute(text, context);
        Assert.True(
            actual == expected,
            $"\"{text}\" became \"{actual}\", expected \"{expected}\"."
        );
    }
}

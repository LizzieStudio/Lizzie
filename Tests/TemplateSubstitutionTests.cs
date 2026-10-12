using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Lizzie.Scripts.Templating;
using Xunit;

public class TemplateSubstitutionTests
{
    private static readonly Column Title = new() { Id = new SnowTag(1), Name = "Title" };
    private static readonly Column Text = new() { Id = new SnowTag(2), Name = "Text" };

    [Fact]
    public void SubstitutedValuesAreNotSubstitutedAgain() =>
        Check("See {Text}", "{Title}", Row((Title, "See {Text}"), (Text, "hello")));

    [Fact]
    public void WidthIsAnOrdinaryColumnName() =>
        Check(
            "wide {Height}",
            "{Width} {Height}",
            Row((new Column { Id = new SnowTag(3), Name = "Width" }, "wide"))
        );

    [Fact]
    public void ColumnsMatchCase() =>
        Check("hello {text}", "{Text} {text}", Row((Title, "x"), (Text, "hello")));

    [Fact]
    public void TheFirstColumnWithANameWins() =>
        Check(
            "first",
            "{Title}",
            Row((Title, "first"), (new Column { Id = new SnowTag(3), Name = "Title" }, "second"))
        );

    [Theory]
    [InlineData("{Typo} stays")]
    [InlineData("{} and { Title } and {Title")]
    public void UnknownKeysStay(string text) => Check(text, text, Row((Title, "x")));

    [Fact]
    public void ColumnsNeedARow() => Check("{Title}", "{Title}", null);

    private static TextureContext Row(params (Column Column, string Value)[] cells)
    {
        var builder = ImmutableDictionary.CreateBuilder<SnowTag, string>();
        foreach (var (column, value) in cells)
            builder[column.Id] = value;
        return new TextureContext
        {
            DataSet = new DataSet
            {
                Id = new SnowTag(10),
                Columns = [.. cells.Select(c => c.Column)],
            },
            CurrentRow = new DataRow { Id = new SnowTag(11), Data = builder.ToImmutable() },
        };
    }

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

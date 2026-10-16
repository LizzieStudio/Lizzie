using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;
using Xunit;

public class DieTests
{
    /// <summary>A die with a suit for each of the <c>|</c>-separated <paramref name="values"/>.</summary>
    private static DieParameters Die(string values, int sideCount = 0) =>
        new()
        {
            SideCount = sideCount,
            Faces = new QuickSide
            {
                Suits = values
                    .Split('|')
                    .Select(v => new QuickSuit { Values = v })
                    .ToImmutableArray(),
            },
        };

    [Theory]
    [InlineData("1", 6)]
    [InlineData("1-2", 6)]
    [InlineData("1-3", 6)]
    [InlineData("1-4", 8)]
    [InlineData("1-5", 10)]
    [InlineData("1-6", 6)]
    [InlineData("1-8", 8)]
    [InlineData("1-10", 10)]
    [InlineData("1-12", 12)]
    [InlineData("1-20", 20)]
    [InlineData("1-3|A-C", 6)]
    [InlineData("", 6)]
    public void AutoPicksTheFewestSidesTheFacesRepeatEvenlyOn(string values, int expected)
    {
        var (sides, error) = Die(values).Resolve(null);
        Assert.True(
            sides == expected && error == null,
            $"Faces \"{values}\" make a d{sides} ({error ?? "no error"}), expected a d{expected}."
        );
    }

    [Theory]
    [InlineData("1-*", 6)]
    [InlineData("1-8, 9*", 10)]
    [InlineData("1-6, 7-*", 8)]
    [InlineData("A|1-*", 6)]
    public void AutoWithAFillingSuitPicksTheFewestSidesWithRoom(string values, int expected)
    {
        var (sides, error) = Die(values).Resolve(null);
        Assert.True(
            sides == expected && error == null,
            $"Faces \"{values}\" make a d{sides} ({error ?? "no error"}), expected a d{expected}."
        );
    }

    [Theory]
    [InlineData("1-7", 0)]
    [InlineData("1-9", 0)]
    [InlineData("1-21", 0)]
    [InlineData("1-21*", 0)]
    [InlineData("1*|2*", 0)]
    [InlineData("1-8", 6)]
    [InlineData("(1", 0)]
    [InlineData("1", 4)]
    public void FacesThatDoNotMakeADieAreAnError(string values, int sideCount)
    {
        var (sides, error) = Die(values, sideCount).Resolve(null);
        Assert.True(
            error != null,
            $"Faces \"{values}\" on {(sideCount == 0 ? "Auto" : $"a d{sideCount}")} make a d{sides} with no "
                + "error, expected an error."
        );
    }

    [Theory]
    [InlineData("1-3", 0, "1, 2, 3, 1, 2, 3")]
    [InlineData("1-3", 8, "1, 2, 3, 1, 2, 3, 1, 2")]
    [InlineData("1, 2-*", 10, "1, 2, 3, 4, 5, 6, 7, 8, 9, 10")]
    [InlineData("1-2|A, B*", 8, "1, 2, A, B, B, B, B, B")]
    [InlineData("|1-2", 0, ", 1, 2, , 1, 2")]
    public void TheFacesFillTheDie(string values, int sideCount, string expected)
    {
        var die = Die(values, sideCount);
        var actual = string.Join(", ", Texts(die.Resolve(null).Sides, (QuickSide)die.Faces));
        Assert.True(
            actual == expected,
            $"Faces \"{values}\" on {(sideCount == 0 ? "Auto" : $"a d{sideCount}")} are [{actual}], "
                + $"expected [{expected}]."
        );
    }

    /// <summary>A side with one suit, <paramref name="suit"/> showing <paramref name="values"/>.</summary>
    private static QuickSide Side(QuickSuit suit, string values) =>
        new() { Suits = [suit with { Values = values }] };

    /// <summary>The objects the die template draws, in drawing order.</summary>
    private static List<TextureFactory.TextureObject> Draw(int sides, QuickSide side)
    {
        var context = new TextureContext { Values = DieTemplate.Values(Colors.White, side, sides) };
        return DieTemplate
            .Template(sides)
            .Elements.Select(TemplateEngine.BuildTemplateElement)
            .Where(e => e.IsVisible(context))
            .SelectMany(e => e.GetElementData(context))
            .ToList();
    }

    /// <summary>The text the die template draws on each face, in order.</summary>
    private static IEnumerable<string> Texts(int sides, QuickSide side) =>
        Draw(sides, side)
            .Where(o => o.Type == TextureFactory.TextureObjectType.Text)
            .Select(o => o.Text);

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(20)]
    public void EachFaceDrawsItsValue(int sides)
    {
        var texts = Texts(sides, Side(new QuickSuit(), "1-*"));
        var expected = Enumerable.Range(1, sides).Select(n => n.ToString());
        Assert.True(
            texts.SequenceEqual(expected),
            $"A d{sides} draws [{string.Join(", ", texts)}], expected [{string.Join(", ", expected)}]."
        );
    }

    [Fact]
    public void AFaceWithoutAnIconDrawsItsValueEvenWithTextOff()
    {
        var texts = Texts(6, Side(new QuickSuit { ShowText = false }, "1-*"));
        Assert.True(
            texts.SequenceEqual(["1", "2", "3", "4", "5", "6"]),
            $"A d6 with Text off draws [{string.Join(", ", texts)}], expected [1, 2, 3, 4, 5, 6]."
        );
    }

    [Fact]
    public void AFaceWithAnIconDrawsThatManyIconsAndNoText()
    {
        var drawn = Draw(6, Side(new QuickSuit { Icon = "Star" }, "3*"));
        var icons = drawn.Where(o => o.Type == TextureFactory.TextureObjectType.CoreShape).ToList();
        var texts = drawn.Where(o =>
            o.Type == TextureFactory.TextureObjectType.Text && o.Text != ""
        );
        Assert.True(
            icons.Count == 6 && icons.All(i => i.Text == "Star" && i.Quantity == 3) && !texts.Any(),
            $"A d6 of \"3*\" with a star draws {icons.Count} icons "
                + $"({string.Join(", ", icons.Select(i => $"{i.Quantity} {i.Text}"))}) and texts "
                + $"[{string.Join(", ", texts.Select(t => t.Text))}], expected 6 icons of 3 Star and no text."
        );
    }

    [Theory]
    [InlineData("D6", 6, false)]
    [InlineData("D8", 8, true)]
    [InlineData("D20", 20, true)]
    [InlineData("Poker", 0, false)]
    [InlineData(null, 0, false)]
    public void ATemplateKnowsItsDie(string size, int sides, bool triangles)
    {
        var template = new Template { Id = SnowTag.Empty, SizeTemplate = size };
        Assert.True(
            template.Sides == sides && template.TriangleFaces == triangles,
            $"A \"{size}\" template has {template.Sides} sides and triangle faces {template.TriangleFaces}, "
                + $"expected {sides} and {triangles}."
        );
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Xunit;

public class QuickTemplateTests
{
    private static readonly Color Cream = Color.Color8(255, 242, 204);
    private static readonly Color Red = Color.Color8(204, 26, 26);

    private static readonly QuickSuit Suit = new() { Background = Cream, Foreground = Red };
    private static readonly QuickSuit Stars = Suit with { Icon = "Star" };

    /// <summary>The objects the Quick template draws for one card, in drawing order.</summary>
    private static List<TextureFactory.TextureObject> Draw(QuickSuit suit, string value)
    {
        var context = new TextureContext { Values = QuickTemplate.Values(suit, value) };
        return QuickTemplate
            .Template.Elements.Select(TemplateEngine.BuildTemplateElement)
            .Where(e => e.IsVisible(context))
            .SelectMany(e => e.GetElementData(context))
            .ToList();
    }

    [Fact]
    public void ACardWithoutAnIconDrawsItsValueLargeOverTheBackground()
    {
        var drawn = Draw(Suit, "No");

        AssertTypes(drawn, Frame, Text, Text);
        var (background, large, corner) = (drawn[0], drawn[1], drawn[2]);
        Assert.True(
            background.BackgroundColor.IsEqualApprox(Cream),
            $"The background is {background.BackgroundColor}, expected {Cream}."
        );
        Assert.True(
            large.Text == "No" && large.ForegroundColor.IsEqualApprox(Red) && corner.Text == "",
            $"The text is \"{large.Text}\" in {large.ForegroundColor} with \"{corner.Text}\" in the corner, "
                + $"expected \"No\" in {Red} with nothing in the corner."
        );
    }

    [Fact]
    public void ACardWithAnIconDrawsItsValueInTheCornerAndThatManyIcons()
    {
        var drawn = Draw(Stars, "3");

        AssertTypes(drawn, Frame, Text, Text, Shape);
        var (large, corner, icons) = (drawn[1], drawn[2], drawn[3]);
        Assert.True(
            large.Text == "" && corner.Text == "3",
            $"The text is \"{large.Text}\" with \"{corner.Text}\" in the corner, expected only \"3\" in the corner."
        );
        Assert.True(
            icons.Text == "Star" && icons.Quantity == 3 && icons.ForegroundColor.IsEqualApprox(Red),
            $"The icons are {icons.Quantity} \"{icons.Text}\" in {icons.ForegroundColor}, "
                + $"expected 3 \"Star\" in {Red}."
        );
    }

    [Theory]
    [InlineData("J")]
    [InlineData("-3")]
    [InlineData("10")]
    public void AValueThatIsNotANumberUpToNineDrawsOneIcon(string value)
    {
        var drawn = Draw(Stars, value);

        AssertTypes(drawn, Frame, Text, Text, Shape);
        Assert.True(
            drawn[3].Quantity == 1,
            $"\"{value}\" draws {drawn[3].Quantity} icons, expected 1."
        );
    }

    [Fact]
    public void ZeroDrawsNoIcons()
    {
        var drawn = Draw(Stars, "0");

        AssertTypes(drawn, Frame, Text, Text);
        Assert.True(drawn[2].Text == "0", $"The corner shows \"{drawn[2].Text}\", expected \"0\".");
    }

    [Fact]
    public void HidingTheTextLeavesOnlyTheIcons()
    {
        var drawn = Draw(Stars with { ShowText = false }, "3");

        AssertTypes(drawn, Frame, Text, Text, Shape);
        Assert.True(
            drawn[1].Text == "" && drawn[2].Text == "" && drawn[3].Quantity == 3,
            $"Drew the text \"{drawn[1].Text}\" and \"{drawn[2].Text}\" with {drawn[3].Quantity} icons, "
                + "expected no text and 3 icons."
        );
    }

    [Fact]
    public void RepeatedCardsAreDrawnOnce()
    {
        var side = new QuickSide { Suits = [Suit with { Values = "A, A, 2" }] };
        var p = new PrintedParameters { Width = 63.5f, Height = 88.9f };

        var drawing = QuickTemplate.Drawing(p, side);
        var rows = Enumerable
            .Range(0, 4)
            .Select(n => QuickTemplate.CardRow(side, n).ToString())
            .ToList();

        Assert.True(
            drawing.Rows.Count == 2 && string.Join(", ", rows) == "1, 1, 2, 1",
            $"Drew {drawing.Rows.Count} cards, shown by cards 1-4 as rows {string.Join(", ", rows)}; "
                + "expected 2 cards, shown as rows 1, 1, 2, 1."
        );
    }

    private const TextureFactory.TextureObjectType Frame = TextureFactory
        .TextureObjectType
        .RectangleFrame;
    private const TextureFactory.TextureObjectType Text = TextureFactory.TextureObjectType.Text;
    private const TextureFactory.TextureObjectType Shape = TextureFactory
        .TextureObjectType
        .CoreShape;

    [StackTraceHidden]
    private static void AssertTypes(
        List<TextureFactory.TextureObject> drawn,
        params TextureFactory.TextureObjectType[] expected
    )
    {
        var actual = drawn.Select(o => o.Type).ToArray();
        Assert.True(
            actual.SequenceEqual(expected),
            $"Drew [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}]."
        );
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Xunit;

public class QuickTemplateTests
{
    private static readonly Color Cream = Color.Color8(255, 242, 204);
    private static readonly Color Red = Color.Color8(204, 26, 26);

    /// <summary>The objects the Quick template draws for one face, in drawing order.</summary>
    private static List<TextureFactory.TextureObject> Draw(QuickTextureField field)
    {
        var context = new TextureContext
        {
            DataSet = QuickTemplate.DataSet,
            CurrentRow = QuickTemplate.Row(Cream, field),
        };
        return QuickTemplate
            .Template.Elements.Select(TemplateEngine.BuildTemplateElement)
            .Where(e => e.IsVisible(context))
            .SelectMany(e => e.GetElementData(context))
            .ToList();
    }

    [Fact]
    public void ATextFaceDrawsItsCaptionOverTheBackground()
    {
        var drawn = Draw(new QuickTextureField { Caption = "No", ForegroundColor = Red });

        AssertTypes(
            drawn,
            TextureFactory.TextureObjectType.RectangleFrame,
            TextureFactory.TextureObjectType.Text
        );
        var (background, text) = (drawn[0], drawn[1]);
        Assert.True(
            background.BackgroundColor.IsEqualApprox(Cream),
            $"The background is {background.BackgroundColor}, expected {Cream}."
        );
        Assert.True(
            text.Text == "No" && text.ForegroundColor.IsEqualApprox(Red),
            $"The text is \"{text.Text}\" in {text.ForegroundColor}, expected \"No\" in {Red}."
        );
    }

    [Fact]
    public void AShapeFaceDrawsItsShapeQuantityTimes()
    {
        var drawn = Draw(
            new QuickTextureField
            {
                FaceType = TextureFactory.TextureObjectType.CoreShape,
                Caption = "Star",
                ForegroundColor = Red,
                Quantity = 3,
            }
        );

        AssertTypes(
            drawn,
            TextureFactory.TextureObjectType.RectangleFrame,
            TextureFactory.TextureObjectType.Text,
            TextureFactory.TextureObjectType.CoreShape
        );
        var (text, shape) = (drawn[1], drawn[2]);
        Assert.True(text.Text == "", $"A shape face also draws the text \"{text.Text}\".");
        Assert.True(
            shape.Text == "Star" && shape.Quantity == 3 && shape.ForegroundColor.IsEqualApprox(Red),
            $"The shape is {shape.Quantity} \"{shape.Text}\" in {shape.ForegroundColor}, "
                + $"expected 3 \"Star\" in {Red}."
        );
    }

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

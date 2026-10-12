using System;
using System.Linq;
using Lizzie.Scripts.Templating;
using Xunit;

public class TemplateElementTests
{
    [Theory]
    [InlineData(typeof(TextElement))]
    [InlineData(typeof(ImageElement))]
    [InlineData(typeof(FrameElement))]
    [InlineData(typeof(TrackElement))]
    public void ANewElementFitsOnTheCard(Type type)
    {
        var element = (TemplateElement)Activator.CreateInstance(type);
        float Get(string name) => float.Parse(element.Parameters.First(p => p.Name == name).Value);
        float x = Get("X"),
            y = Get("Y"),
            halfWidth = Get("Width") / 2,
            halfHeight = Get("Height") / 2;

        Assert.True(
            x - halfWidth >= 0
                && x + halfWidth <= 100
                && y - halfHeight >= 0
                && y + halfHeight <= 100,
            $"A new {type.Name} spans x {x - halfWidth}..{x + halfWidth}, y {y - halfHeight}..{y + halfHeight}, "
                + "expected it inside the card, 0..100 percent."
        );
    }

    [Theory]
    [InlineData("16", 16)]
    [InlineData("16.5", 17)]
    [InlineData("16.4", 16)]
    [InlineData("-2.5", -3)]
    [InlineData("16,5", 0)]
    [InlineData("wide", 0)]
    public void NumbersRoundToTheNearestWhole(string value, int expected)
    {
        var element = new TextElement();
        element.SetParameterValue("X", value);

        var actual = element.EvaluateNumberParameter(element.Parameters, "X", new TextureContext());
        Assert.True(actual == expected, $"\"{value}\" evaluated to {actual}, expected {expected}.");
    }
}

using System;
using System.Collections.Immutable;
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

    [Fact]
    public void ImagesCarryABoundQuantity()
    {
        var pips = new Column { Id = new SnowTag(1), Name = "Pips" };
        var context = new TextureContext
        {
            DataSet = new DataSet { Id = new SnowTag(10), Columns = [pips] },
            CurrentRow = new DataRow
            {
                Id = new SnowTag(11),
                Data = ImmutableDictionary<SnowTag, string>.Empty.Add(pips.Id, "3"),
            },
        };
        var element = new ImageElement();
        element.SetParameterValue("Quantity", "{Pips}");

        var shape = element.GetElementData(context).Single();
        Assert.True(
            shape.Quantity == 3,
            $"An image with Quantity {{Pips}} = 3 drew {shape.Quantity}."
        );
    }

    [Fact]
    public void ANewImageDrawsOneShape()
    {
        var shape = new ImageElement().GetElementData(new TextureContext()).Single();
        Assert.True(shape.Quantity == 1, $"A new image drew {shape.Quantity}, expected 1.");
    }
}

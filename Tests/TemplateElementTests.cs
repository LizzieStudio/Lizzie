using System;
using System.Collections.Generic;
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
    public void PositionsKeepFractionsOfAPercent()
    {
        var element = new TextElement();
        element.SetParameterValue("X", "16.5");

        var drawn = element.GetElementData(new TextureContext()).Single();
        Assert.True(drawn.CenterX == 16.5f, $"X \"16.5\" drew at {drawn.CenterX}, expected 16.5.");
    }

    [Fact]
    public void TrackBoxesFillTheTrack()
    {
        var track = new TrackElement();
        track.SetParameterValue("Track Type", "Horizontal");
        track.SetParameterValue("X", "50");
        track.SetParameterValue("Width", "100");
        track.SetParameterValue("# of Boxes", "3");

        // The first frame is the whole track; the rest are its boxes.
        var boxes = track.GetElementData(new TextureContext()).Skip(1).ToList();
        float left = boxes.Min(b => b.CenterX - b.Width / 2),
            right = boxes.Max(b => b.CenterX + b.Width / 2);
        Assert.True(
            Math.Abs(left) < 0.001 && Math.Abs(right - 100) < 0.001,
            $"3 boxes across a 100% track span {left}..{right}, expected 0..100."
        );
    }

    [Fact]
    public void ImagesCarryABoundQuantity()
    {
        var context = new TextureContext
        {
            Values = new Dictionary<string, string> { ["Pips"] = "3" },
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

    [Theory]
    [InlineData("True", true)]
    [InlineData("Yes", true)]
    [InlineData("3", true)]
    [InlineData("anything", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("F", false)]
    [InlineData("No", false)]
    [InlineData("n", false)]
    public void AYesNoValueIsOffOnlyWhenBlankZeroFalseOrNo(string value, bool expected)
    {
        var actual = TemplateElement.IsTruthy(value);
        Assert.True(
            actual == expected,
            $"\"{value}\" is {(actual ? "on" : "off")}, expected {(expected ? "on" : "off")}."
        );
    }

    [Theory]
    [InlineData(typeof(TextElement))]
    [InlineData(typeof(ImageElement))]
    [InlineData(typeof(FrameElement))]
    [InlineData(typeof(TrackElement))]
    public void ANewElementIsVisible(Type type)
    {
        var element = (TemplateElement)Activator.CreateInstance(type);
        Assert.True(element.IsVisible(new TextureContext()), $"A new {type.Name} is hidden.");
    }

    [Theory]
    [InlineData("3", true)]
    [InlineData("", false)]
    public void VisibleFollowsABoundColumn(string cell, bool expected)
    {
        var context = new TextureContext
        {
            Values = new Dictionary<string, string> { ["Pips"] = cell },
        };
        var element = new ImageElement();
        element.SetParameterValue("Visible", "{Pips}");

        var actual = element.IsVisible(context);
        Assert.True(
            actual == expected,
            $"An image with Visible {{Pips}} = \"{cell}\" is {(actual ? "shown" : "hidden")}."
        );
    }

    [Theory]
    [InlineData("Vertical", "4", "1*", "[1, 1, 1, 1]")]
    [InlineData("Vertical", "4", "1, 2, 3", "[1, 2, 3, 1]")]
    [InlineData("Vertical", "4", "1-6", "[1, 2, 3, 4]")]
    [InlineData("Vertical", "4", "Start, 2-*", "[Start, 2, 3, 4]")]
    [InlineData("Vertical", "4", "", "[]")]
    [InlineData("Perimeter", "4", "A*", "[A, A, A, A, A, A, A, A]")]
    public void BoxTextIsFittedToTheBoxes(
        string trackType,
        string boxes,
        string text,
        string expected
    )
    {
        var element = new TrackElement();
        element.SetParameterValue("Track Type", trackType);
        element.SetParameterValue("# of Boxes", boxes);
        element.SetParameterValue("Box Text", text);

        var texts = element
            .GetElementData(new TextureContext())
            .Where(o => o.Type == TextureFactory.TextureObjectType.Text)
            .Select(o => o.Text);
        var actual = $"[{string.Join(", ", texts)}]";
        Assert.True(
            actual == expected,
            $"A {trackType} track with {boxes} boxes and Box Text \"{text}\" shows {actual}, expected {expected}."
        );
    }
}

using Xunit;

namespace Lizzie.Tests;

public class TemplateTests
{
    [Theory]
    [InlineData(2.5f, 3.5f, 250, 350)]
    [InlineData(2.56f, 2.35f, 256, 235)]
    public void PixelsRoundToTheNearestWhole(
        float width,
        float height,
        int pixelsWide,
        int pixelsHigh
    )
    {
        var actual = new Template
        {
            Id = new SnowTag(1),
            Width = width,
            Height = height,
        }.Pixels(100);
        Assert.True(
            actual == (pixelsWide, pixelsHigh),
            $"A {width} x {height} in template is {actual} px at 100 per inch, expected ({pixelsWide}, {pixelsHigh})."
        );
    }

    [Theory]
    [InlineData("D6", 512, 340)]
    [InlineData("D20", 512, 512)]
    public void ADieTemplateIsTheSizeOfItsNet(string size, int pixelsWide, int pixelsHigh)
    {
        var actual = new Template { Id = new SnowTag(1), SizeTemplate = size }.Pixels(100);
        Assert.True(
            actual == (pixelsWide, pixelsHigh),
            $"A {size} template is {actual} px at 100 per inch, expected ({pixelsWide}, {pixelsHigh})."
        );
    }
}

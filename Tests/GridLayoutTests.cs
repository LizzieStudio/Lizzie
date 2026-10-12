using System.Diagnostics;
using System.Text.Json;
using Lizzie.AssetManagement;
using Lizzie.Replication.Machinery;
using Xunit;

public class GridLayoutTests
{
    [Theory]
    [InlineData(8, 8, null, 64)]
    [InlineData(8, 8, 56, 56)]
    [InlineData(8, 8, 100, 64)]
    [InlineData(1, 1, null, 1)]
    public void CountsFrames(int columns, int rows, int? count, int frames) =>
        Assert.Equal(frames, new GridLayout(columns, rows, count).FrameCount());

    [Theory]
    [InlineData(1, 0)]
    [InlineData(56, 55)]
    [InlineData(0, null)]
    [InlineData(57, null)]
    public void NamesFramesFromOne(int value, int? frame) =>
        Assert.Equal(frame, new GridLayout(8, 8, 56).Frame(value));

    [Theory]
    [InlineData("", null, "[1, 2, 3, 4, 5, 6]")]
    [InlineData("  ", 4, "[1, 2, 3, 4]")]
    [InlineData("", 8, "[1, 2, 3, 4, 5, 6, 1, 2]")]
    [InlineData("2, 5*2", null, "[2, 5, 5]")]
    public void BlankMeansEveryFrame(string expression, int? count, string expected)
    {
        Assert.True(new GridLayout(3, 2).TryParse(expression, out var values, count));
        Check(expected, values);
    }

    [Fact]
    public void AssetsDefaultToOneFrame() =>
        Assert.Equal(GridLayout.Single, new ImageAsset { Id = new SnowTag(1) }.Layout);

    [Fact]
    public void BlankUsesTheFirstFramesOfAnImageOverTheCap()
    {
        bool parsed = new GridLayout(101, 100).TryParse("", out var values);
        Assert.True(
            parsed && values.Length == RangeExpressions.MaxValues,
            $"A blank expression on 10,100 frames returned {parsed} with {values.Length} values, "
                + $"expected true with {RangeExpressions.MaxValues}."
        );
    }

    [Fact]
    public void AnAssetWithoutAPathIsNotDownloaded() =>
        Assert.False(AssetImageCache.Instance.IsDownloaded(new ImageAsset { Id = new SnowTag(1) }));

    [Fact]
    public void LayoutSurvivesSaving()
    {
        var asset = new ImageAsset { Id = new SnowTag(1), Layout = new GridLayout(8, 7, 56) };

        var json = JsonSerializer.Serialize<Replicated>(asset, LizzieJson.EventOptions);
        var back = (ImageAsset)
            JsonSerializer.Deserialize<Replicated>(json, LizzieJson.EventOptions);

        Assert.True(
            back.Layout == asset.Layout,
            $"{json} loaded as {back.Layout}, expected {asset.Layout}."
        );
    }

    [StackTraceHidden]
    private static void Check(string expected, int[] values)
    {
        var actual = $"[{string.Join(", ", values)}]";
        Assert.True(actual == expected, $"Got {actual}, expected {expected}.");
    }
}

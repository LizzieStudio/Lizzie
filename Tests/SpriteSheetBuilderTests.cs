using Xunit;

public class SpriteSheetBuilderTests
{
    [Theory]
    [InlineData(250, 350, 1, 252, 352)]
    [InlineData(252, 352, 52, 252, 352)]
    [InlineData(157, 157, 3, 160, 160)]
    public void CellsAreRoundedUpToAMultipleOfFour(
        int width,
        int height,
        int count,
        int cellWidth,
        int cellHeight
    )
    {
        var actual = SpriteSheetBuilder.Grid(width, height, count);
        Assert.True(
            (actual.CellWidth, actual.CellHeight) == (cellWidth, cellHeight),
            $"{count} cells of {width} x {height} are drawn at {actual.CellWidth} x {actual.CellHeight}, "
                + $"expected {cellWidth} x {cellHeight}."
        );
    }

    [Theory]
    [InlineData(1181, 1181, 200)]
    [InlineData(400, 20000, 1)]
    [InlineData(250, 350, 5000)]
    public void ABigSheetFitsInOneTexture(int width, int height, int count)
    {
        var (layout, cellWidth, cellHeight) = SpriteSheetBuilder.Grid(width, height, count);
        int sheetWidth = layout.Columns * cellWidth,
            sheetHeight = layout.Rows * cellHeight;
        Assert.True(
            sheetWidth <= SpriteSheetBuilder.MaxSize
                && sheetHeight <= SpriteSheetBuilder.MaxSize
                && cellWidth % 4 == 0
                && cellHeight % 4 == 0
                && cellWidth > 0
                && cellHeight > 0,
            $"{count} cells of {width} x {height} make a {sheetWidth} x {sheetHeight} sheet of "
                + $"{cellWidth} x {cellHeight} cells, expected at most {SpriteSheetBuilder.MaxSize} "
                + "on a side, with cells a positive multiple of 4."
        );
    }
}

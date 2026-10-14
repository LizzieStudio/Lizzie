using Xunit;

public class SheetCellsTests
{
    private static readonly SnowTag A = new(1);
    private static readonly SnowTag B = new(2);
    private static readonly SnowTag C = new(3);

    [Fact]
    public void ARowKeepsItsFrame()
    {
        var cells = new SheetCells();
        int a = cells.Frame(A);
        int b = cells.Frame(B);
        cells.Frame(C);
        Assert.True(
            (cells.Frame(A), cells.Frame(B)) == (a, b),
            $"Rows moved from frames {a} and {b} to {cells.Frame(A)} and {cells.Frame(B)} after another row was added."
        );
    }

    [Fact]
    public void NewRowsGetTheNextFrame()
    {
        var cells = new SheetCells();
        var frames = (cells.Frame(B), cells.Frame(A), cells.Frame(C));
        Assert.True(
            frames == (0, 1, 2),
            $"Three new rows got frames {frames}, expected (0, 1, 2)."
        );
    }

    [Fact]
    public void OnlyChangedFramesAreStale()
    {
        var cells = new SheetCells();
        cells.Frame(A);
        cells.Frame(B);
        cells.Frame(C);
        cells.Drew(0, "a1");
        cells.Drew(1, "b1");
        cells.Drew(2, "c1");
        var stale = cells.Stale(["a1", "b2", "c1"]);
        Assert.True(
            stale is [1],
            $"Changing frame 1's version made frames [{string.Join(", ", stale)}] stale, expected [1]."
        );
    }

    [Fact]
    public void UndrawnFramesAreStale()
    {
        var cells = new SheetCells();
        cells.Frame(A);
        cells.Frame(B);
        cells.Drew(0, "a1");
        var stale = cells.Stale(["a1", "b1"]);
        Assert.True(
            stale is [1] && !cells.AllDrawn,
            $"With frame 1 never drawn, frames [{string.Join(", ", stale)}] are stale and AllDrawn is "
                + $"{cells.AllDrawn}, expected [1] and false."
        );
    }

    [Fact]
    public void ANewRowNeedsDrawing()
    {
        var cells = new SheetCells();
        cells.Frame(A);
        cells.Drew(0, "a1");
        bool before = cells.AllDrawn;
        cells.Frame(B);
        Assert.True(
            before && !cells.AllDrawn,
            $"AllDrawn was {before} before a new row and {cells.AllDrawn} after, expected true then false."
        );
    }
}

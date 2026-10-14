using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

public static class SpriteSheetBuilder
{
    /// <summary>
    /// The widest or tallest sheet.
    /// </summary>
    /// <remarks>
    /// 16384 squared is <see cref="Image"/>'s pixel limit.
    /// </remarks>
    public const int MaxSize = 16384;

    /// <summary>
    /// The grid for <paramref name="count"/> cells of <paramref name="width"/> by <paramref name="height"/> pixels,
    /// as square as it can be, and the size each cell is drawn at.
    /// </summary>
    /// <remarks>
    /// Compressed images are padded to a multiple of 4 pixels, so cells are rounded up to one.
    /// A sheet larger than <see cref="MaxSize"/> draws its cells smaller, still a multiple of 4.
    /// </remarks>
    public static (GridLayout Layout, int CellWidth, int CellHeight) Grid(
        int width,
        int height,
        int count
    )
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling((double)count / columns);
        int cellWidth = (width + 3) / 4 * 4;
        int cellHeight = (height + 3) / 4 * 4;
        float scale = Math.Min(
            MaxSize / (float)(columns * cellWidth),
            MaxSize / (float)(rows * cellHeight)
        );
        if (scale < 1)
            (cellWidth, cellHeight) = (
                (int)(cellWidth * scale) / 4 * 4,
                (int)(cellHeight * scale) / 4 * 4
            );
        return (new GridLayout(columns, rows, count), cellWidth, cellHeight);
    }

    /// <summary>
    /// Draws each of <paramref name="cells"/> into its own cell of a <paramref name="layout"/> grid.
    /// </summary>
    /// <returns>The sheet, or null if <paramref name="token"/> was canceled first.</returns>
    public static async Task<Texture2D> Build(
        TextureFactory factory,
        List<TextureFactory.TextureDefinition> cells,
        GridLayout layout,
        int cellWidth,
        int cellHeight,
        CancellationToken token
    )
    {
        var sheet = Image.CreateEmpty(
            cellWidth * layout.Columns,
            cellHeight * layout.Rows,
            false,
            Image.Format.Rgba8
        );
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = await factory.Render(cells[i], token);
            if (token.IsCancellationRequested)
                return null;
            if (cell != null)
                sheet.BlitRect(
                    cell,
                    new Rect2I(Vector2I.Zero, cell.GetSize()),
                    new Vector2I(i % layout.Columns * cellWidth, i / layout.Columns * cellHeight)
                );
        }

        sheet.GenerateMipmaps();
        sheet.Compress(Image.CompressMode.S3Tc, Image.CompressSource.Generic);
        return ImageTexture.CreateFromImage(sheet);
    }
}

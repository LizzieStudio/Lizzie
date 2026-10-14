using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

public class SpriteSheetBuilder
{
    private readonly TextureFactory _factory;
    private readonly List<TextureFactory.TextureDefinition> _defs;
    private readonly int _hframes;
    private readonly int _vframes;
    private readonly int _cellW;
    private readonly int _cellH;
    private readonly TaskCompletionSource<Texture2D> _done = new();
    private Image _sheet;
    private int _nextIndex;

    public SpriteSheetBuilder(
        TextureFactory factory,
        List<TextureFactory.TextureDefinition> defs,
        int cellW,
        int cellH,
        int hframes,
        int vframes
    )
    {
        _factory = factory;
        _defs = defs;
        _cellW = cellW;
        _cellH = cellH;
        _hframes = Math.Max(hframes, 1);
        _vframes = Math.Max(vframes, 1);
    }

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

    public Task<Texture2D> Start()
    {
        _sheet = Image.CreateEmpty(_cellW * _hframes, _cellH * _vframes, false, Image.Format.Rgba8);
        QueueNext();
        return _done.Task;
    }

    private void QueueNext()
    {
        if (_nextIndex >= _defs.Count)
        {
            Finish();
            return;
        }

        int idx = _nextIndex++;
        _factory.GenerateTexture(_defs[idx], tex => OnCellReady(idx, tex));
    }

    private void OnCellReady(int index, ImageTexture tex)
    {
        if (tex != null)
        {
            var cellImg = tex.GetImage();
            if (cellImg != null)
            {
                int col = index % _hframes;
                int row = index / _hframes;
                _sheet.BlitRect(
                    cellImg,
                    new Rect2I(0, 0, cellImg.GetWidth(), cellImg.GetHeight()),
                    new Vector2I(col * _cellW, row * _cellH)
                );
            }
        }

        QueueNext();
    }

    private void Finish()
    {
        _sheet.GenerateMipmaps();
        _sheet.Compress(Image.CompressMode.S3Tc, Image.CompressSource.Generic);

        _done.SetResult(ImageTexture.CreateFromImage(_sheet));
    }
}

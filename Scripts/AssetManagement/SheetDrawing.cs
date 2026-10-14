using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// Used to keep track of what a <see cref="Sheet"/> rendered.
/// </summary>
public readonly record struct Drawing(
    Template Template,
    DataSet DataSet,
    IReadOnlyList<DataRow> Rows
);

/// <summary>
/// Uses a <see cref="RecordService.Watch"/> call to keep a <see cref="Sheet"/> up to date.
/// </summary>
/// <remarks>
/// The batch of frames being drawn finishes even when its records change again,
/// and the next batch draws whatever changed in the meantime.
/// </remarks>
public partial class SheetDrawing : Node
{
    private const float PixelsPerInch = 100;

    public Sheet Sheet { get; init; }
    public TextureFactory Factory { get; init; }

    /// <summary>
    /// Reads what the sheet shows. It reruns whenever a record it read changes.
    /// </summary>
    public Func<IRecordReader, Drawing> Source { get; init; }

    /// <summary>
    /// Stops drawing when canceled.
    /// </summary>
    public CancellationToken Token { get; init; }

    private readonly SheetCells _cells = new();

    // What the last sync asked for, until a batch starts drawing it.
    private Batch _wanted;
    private bool _drawing;

    // Completes once every frame asked for so far has been drawn.
    private TaskCompletionSource _allDrawn;

    // Every frame as last drawn, uncompressed so redrawing a frame doesn't degrade the others,
    // and the batch whose layout it has.
    private Image _image;
    private Batch _laidOut;

    /// <param name="Versions">What each frame should be drawn from.</param>
    /// <param name="Draws">Makes each frame's drawing, or null for the checkerboard.</param>
    private sealed record Batch(
        GridLayout Layout,
        int CellWidth,
        int CellHeight,
        string[] Versions,
        Func<TextureFactory.TextureDefinition>[] Draws
    )
    {
        public Vector2I Position(int frame) =>
            new(frame % Layout.Columns * CellWidth, frame / Layout.Columns * CellHeight);
    }

    public override void _EnterTree() => RecordService.Instance.Watch(this, Sync);

    /// <summary>
    /// The frame that shows <paramref name="row"/>. A row keeps its frame for the life of the sheet.
    /// </summary>
    public int Frame(SnowTag row)
    {
        int count = _cells.Rows.Count;
        int frame = _cells.Frame(row);
        if (_cells.Rows.Count > count)
            RecordService.Instance.QueueSync(this);
        return frame;
    }

    /// <summary>
    /// Completes once every frame has been drawn.
    /// </summary>
    public Task Loaded() => _cells.AllDrawn ? Task.CompletedTask : (_allDrawn ??= new()).Task;

    private void Sync(IRecordReader R)
    {
        var (template, dataSet, rows) = Source(R);
        var (width, height) = template?.Pixels(PixelsPerInch) ?? default;
        bool drawable = dataSet != null && width > 0 && height > 0;
        if (!drawable)
            (width, height) = (Sheet.Missing.Texture.GetWidth(), Sheet.Missing.Texture.GetHeight());

        var (layout, cellWidth, cellHeight) = SpriteSheetBuilder.Grid(
            width,
            height,
            _cells.Rows.Count
        );
        var byId = rows.ToDictionary(r => r.Id);
        var versions = new string[_cells.Rows.Count];
        var draws = new Func<TextureFactory.TextureDefinition>[_cells.Rows.Count];
        for (int frame = 0; frame < versions.Length; frame++)
        {
            var row = drawable ? byId.GetValueOrDefault(_cells.Rows[frame]) : null;
            versions[frame] =
                $"{cellWidth}x{cellHeight}:"
                + (
                    row == null
                        ? "missing"
                        : template.SheetKey() + dataSet.SheetKey() + row.SheetKey()
                );
            if (row != null)
                draws[frame] = () =>
                    TemplateEngine.GenerateTextureDefinition(
                        template,
                        new TextureContext
                        {
                            CurrentRow = row,
                            DataSet = dataSet,
                            Dpi = PixelsPerInch,
                            ParentSize = new Vector2(cellWidth, cellHeight),
                        }
                    );
        }
        _wanted = new Batch(layout, cellWidth, cellHeight, versions, draws);
        if (!_drawing)
            Draw();
    }

    private async void Draw()
    {
        _drawing = true;
        while (_wanted is { } batch)
        {
            _wanted = null;
            var stale = _cells.Stale(batch.Versions);
            if (stale.Count > 0)
            {
                LayOut(batch);
                foreach (int frame in stale)
                {
                    var cell = await DrawFrame(batch, frame);
                    if (Token.IsCancellationRequested)
                        return;
                    _image.BlitRect(
                        cell,
                        new Rect2I(Vector2I.Zero, cell.GetSize()),
                        batch.Position(frame)
                    );
                    _cells.Drew(frame, batch.Versions[frame]);
                }

                var shown = (Image)_image.Duplicate();
                shown.GenerateMipmaps();
                shown.Compress(Image.CompressMode.S3Tc, Image.CompressSource.Generic);
                Sheet.Show(ImageTexture.CreateFromImage(shown), batch.Layout);
            }
            // Frames asked for during the batch queued another sync, so another batch draws them.
            if (_cells.AllDrawn)
            {
                _allDrawn?.SetResult();
                _allDrawn = null;
            }
        }
        _drawing = false;
    }

    /// <summary>
    /// Fits <see cref="_image"/> to <paramref name="batch"/>'s layout, moving the frames already drawn.
    /// </summary>
    private void LayOut(Batch batch)
    {
        bool sameCells =
            _laidOut != null
            && _laidOut.CellWidth == batch.CellWidth
            && _laidOut.CellHeight == batch.CellHeight;
        if (
            sameCells
            && _laidOut.Layout.Columns == batch.Layout.Columns
            && _laidOut.Layout.Rows == batch.Layout.Rows
        )
        {
            _laidOut = batch;
            return;
        }

        var image = Image.CreateEmpty(
            batch.CellWidth * batch.Layout.Columns,
            batch.CellHeight * batch.Layout.Rows,
            false,
            Image.Format.Rgba8
        );
        // Frames drawn at another size are all stale, so there's nothing to keep.
        if (sameCells)
            for (int frame = 0; frame < _laidOut.Versions.Length; frame++)
                image.BlitRect(
                    _image,
                    new Rect2I(
                        _laidOut.Position(frame),
                        new Vector2I(batch.CellWidth, batch.CellHeight)
                    ),
                    batch.Position(frame)
                );
        _image = image;
        _laidOut = batch;
    }

    /// <summary>
    /// Draws <paramref name="frame"/>, or the checkerboard when it has nothing to draw or drawing fails.
    /// </summary>
    private async Task<Image> DrawFrame(Batch batch, int frame)
    {
        Image cell = null;
        if (batch.Draws[frame] is { } draw)
            try
            {
                cell = await Factory.Render(draw(), Token);
            }
            catch (Exception e)
            {
                GD.PrintErr(e);
            }
        if (cell != null)
            return cell;

        var missing = Sheet.Missing.Texture.GetImage();
        missing.Convert(Image.Format.Rgba8);
        missing.Resize(batch.CellWidth, batch.CellHeight, Image.Interpolation.Nearest);
        return missing;
    }
}

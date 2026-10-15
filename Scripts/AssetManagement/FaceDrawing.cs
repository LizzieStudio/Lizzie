using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

/// <summary>
/// What a drawn <see cref="FaceSet"/> shows.
/// </summary>
public readonly record struct Drawing(
    Template Template,
    DataSet DataSet,
    IReadOnlyList<DataRow> Rows
);

/// <summary>
/// Uses a <see cref="RecordService.Watch"/> call to keep the faces of a <see cref="FaceSet"/> up to date.
/// </summary>
/// <remarks>
/// Faces are drawn one at a time. The face being drawn finishes even when its records change again,
/// and is drawn again afterward.
/// </remarks>
public partial class FaceDrawing : Node
{
    private const float PixelsPerInch = 100;

    public FaceSet FaceSet { get; init; }
    public TextureFactory Factory { get; init; }

    /// <summary>
    /// Reads what the faces show. It reruns whenever a record it read changes.
    /// </summary>
    public Func<IRecordReader, Drawing> Source { get; init; }

    /// <summary>
    /// Stops drawing when canceled.
    /// </summary>
    public CancellationToken Token { get; init; }

    // A row's face, and the version it was last drawn from, or null before it's drawn.
    private sealed class Cell(Face face)
    {
        public Face Face { get; } = face;
        public string Drawn { get; set; }
    }

    /// <param name="Version">What the face should be drawn from.</param>
    /// <param name="Draw">Makes the face's drawing, or null for the checkerboard.</param>
    private sealed record Wanted(string Version, Func<TextureFactory.TextureDefinition> Draw);

    private readonly Dictionary<SnowTag, Cell> _cells = new();

    // What the last sync asked each face to show.
    private Dictionary<SnowTag, Wanted> _wanted = new();
    private bool _drawing;

    public override void _EnterTree() => RecordService.Instance.Watch(this, Sync);

    /// <summary>
    /// The face that shows <paramref name="row"/>, drawn once a sync reads the row.
    /// </summary>
    public Face Face(SnowTag row)
    {
        if (!_cells.TryGetValue(row, out var cell))
        {
            _cells[row] = cell = new Cell(new Face(FaceSet));
            RecordService.Instance.QueueSync(this);
        }
        return cell.Face;
    }

    private void Sync(IRecordReader R)
    {
        var (template, dataSet, rows) = Source(R);
        var (width, height) = template?.Pixels(PixelsPerInch) ?? default;
        // Compressing pads an image to a multiple of 4 pixels by smearing its edges, so draw at one instead.
        (width, height) = ((width + 3) / 4 * 4, (height + 3) / 4 * 4);
        bool drawable = dataSet != null && width > 0 && height > 0;
        var byId = rows.ToDictionary(r => r.Id);
        _wanted = _cells.Keys.ToDictionary(
            row => row,
            row =>
                (drawable ? byId.GetValueOrDefault(row) : null) is { } data
                    ? new Wanted(
                        $"{width}x{height}:"
                            + template.VersionKey()
                            + dataSet.VersionKey()
                            + data.VersionKey(),
                        () =>
                            TemplateEngine.GenerateTextureDefinition(
                                template,
                                new TextureContext
                                {
                                    CurrentRow = data,
                                    DataSet = dataSet,
                                    Dpi = PixelsPerInch,
                                    ParentSize = new Vector2(width, height),
                                    TriangleFaces = template.TriangleFaces,
                                }
                            )
                    )
                    : new Wanted("missing", null)
        );
        if (!_drawing)
            Draw();
    }

    private async void Draw()
    {
        _drawing = true;
        while (Stale() is (Cell cell, Wanted wanted))
        {
            Image image = null;
            if (wanted.Draw != null)
                try
                {
                    image = await Factory.Render(wanted.Draw(), Token);
                }
                catch (Exception e)
                {
                    GD.PrintErr(e);
                }
            if (Token.IsCancellationRequested)
                return;
            cell.Face.Show(image);
            cell.Drawn = wanted.Version;
        }
        _drawing = false;
    }

    // The first face that isn't drawn from what the last sync asked for, and what it asked for.
    private (Cell, Wanted)? Stale()
    {
        foreach (var (row, cell) in _cells)
            if (_wanted.TryGetValue(row, out var wanted) && cell.Drawn != wanted.Version)
                return (cell, wanted);
        return null;
    }
}

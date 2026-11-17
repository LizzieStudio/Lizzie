using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Godot;

namespace Lizzie;

/// <summary>
/// What a drawn <see cref="FaceSet"/> shows.
/// </summary>
public readonly record struct Drawing(
    Template Template,
    IReadOnlyDictionary<SnowTag, IReadOnlyDictionary<string, string>> Rows
);

/// <summary>
/// Uses a <see cref="Atoms.Watch"/> call to keep the faces of a <see cref="FaceSet"/> up to date.
/// </summary>
/// <remarks>
/// Faces are drawn one at a time. The face being drawn finishes even when its atoms change again,
/// and is drawn again afterward.
/// </remarks>
public partial class FaceDrawing : Node
{
    private const float PixelsPerInch = 100;

    public FaceSet FaceSet { get; init; }

    /// <summary>
    /// Reads what the faces show. It reruns whenever an atom it read changes.
    /// </summary>
    public Func<IAtomReader, Drawing> Source { get; init; }

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

    public override void _EnterTree() => Atoms.R.Watch(this, Sync);

    /// <summary>
    /// The face that shows <paramref name="row"/>, drawn once a sync reads the row.
    /// </summary>
    public Face Face(SnowTag row)
    {
        if (!_cells.TryGetValue(row, out var cell))
        {
            _cells[row] = cell = new Cell(new Face(FaceSet));
            Atoms.R.QueueSync(this);
        }
        return cell.Face;
    }

    private void Sync(IWatchReader R)
    {
        var (template, rows) = Source(R);
        var (width, height) = template?.Pixels(PixelsPerInch) ?? default;
        bool drawable = width > 0 && height > 0;
        _wanted = _cells.Keys.ToDictionary(
            row => row,
            row =>
                (drawable ? rows.GetValueOrDefault(row) : null) is { } values
                    ? new Wanted(
                        $"{width}x{height}:"
                            + R.VersionKey(template)
                            + JsonSerializer.Serialize(
                                values.OrderBy(v => v.Key, StringComparer.Ordinal)
                            ),
                        () =>
                            TemplateEngine.GenerateTextureDefinition(
                                template,
                                new TextureContext
                                {
                                    Values = values,
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
                    image = await TextureFactory.Instance.Render(wanted.Draw(), Token);
                }
                catch (Exception e)
                {
                    GD.PrintErr(e);
                }
            if (Token.IsCancellationRequested)
                return;
            await cell.Face.Show(image);
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

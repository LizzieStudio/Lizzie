using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The frames of a drawn sheet.
/// </summary>
public sealed class SheetCells
{
    private readonly Dictionary<SnowTag, int> _frames = new();
    private readonly List<SnowTag> _rows = new();

    // The version each frame was last drawn from, or null if it hasn't been drawn.
    private readonly List<string> _drawn = new();

    /// <summary>
    /// The row each frame shows.
    /// </summary>
    public IReadOnlyList<SnowTag> Rows => _rows;

    /// <summary>
    /// Whether every frame has been drawn at least once.
    /// </summary>
    public bool AllDrawn => !_drawn.Contains(null);

    /// <summary>
    /// The frame that shows <paramref name="row"/>, adding one after the others for a new row.
    /// A row keeps its frame.
    /// </summary>
    public int Frame(SnowTag row)
    {
        if (!_frames.TryGetValue(row, out int frame))
        {
            _frames[row] = frame = _rows.Count;
            _rows.Add(row);
            _drawn.Add(null);
        }
        return frame;
    }

    /// <summary>
    /// The frames that weren't last drawn from their version in <paramref name="versions"/>.
    /// </summary>
    public List<int> Stale(IReadOnlyList<string> versions) =>
        Enumerable.Range(0, versions.Count).Where(f => _drawn[f] != versions[f]).ToList();

    /// <summary>
    /// Records that <paramref name="frame"/> was drawn from <paramref name="version"/>.
    /// </summary>
    public void Drew(int frame, string version) => _drawn[frame] = version;
}

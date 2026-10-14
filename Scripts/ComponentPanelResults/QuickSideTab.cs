using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// Edits a <see cref="QuickSide"/>, with a row for each suit.
/// </summary>
public partial class QuickSideTab : SideTab
{
    [Export]
    private SpinBox _suitCount;

    [Export]
    private VBoxContainer _suits;

    [Export]
    private PackedScene _suitRow;

    private IEnumerable<QuickSuitRow> Rows => _suits.GetChildren().OfType<QuickSuitRow>();

    public override void _Ready()
    {
        foreach (var row in Rows)
            row.Changed += RaiseChanged;
        _suitCount.ValueChanged += count =>
        {
            Resize((int)count);
            RaiseChanged();
        };
    }

    /// <summary>
    /// Adds or removes rows
    ///
    /// <para>Added rows copy the one above it with a new color.</para>
    /// </summary>
    private void Resize(int count)
    {
        var rows = Rows.ToList();
        for (int i = rows.Count; i < count; i++)
        {
            var above = rows[^1].Suit;
            var row = _suitRow.Instantiate<QuickSuitRow>();
            _suits.AddChild(row);
            row.Suit = above with { Background = NextColor(above.Background) };
            row.Changed += RaiseChanged;
            rows.Add(row);
        }
        foreach (var row in rows.Skip(count))
        {
            _suits.RemoveChild(row);
            row.QueueFree();
        }
    }

    /// <summary>
    /// A new hue, keeping the color's lightness and saturation.
    /// </summary>
    /// <remarks>
    /// 13f / 34f is a rough approximation of the golden angle.
    /// It's a simple way to produce (up to 34) distinct colors on a color wheel.
    /// </remarks>
    private static Color NextColor(Color above) =>
        Color.FromOkHsl(above.OkHslH + (13f / 34f), above.OkHslS, above.OkHslL);

    public override SideParameters Side =>
        new QuickSide { Suits = Rows.Select(row => row.Suit).ToImmutableArray() };

    public override bool TryLoad(SideParameters side)
    {
        if (side is not QuickSide quick)
            return false;
        int count = Math.Max(quick.Suits.Length, 1);
        _suitCount.SetValueNoSignal(count);
        Resize(count);
        foreach (var (row, suit) in Rows.Zip(quick.Suits))
            row.Suit = suit;
        return true;
    }
}

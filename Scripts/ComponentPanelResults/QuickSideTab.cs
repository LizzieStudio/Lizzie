using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Edits a <see cref="QuickSide"/>, with a row for each suit.
/// </summary>
public partial class QuickSideTab : SideTab
{
    [Export]
    private VBoxContainer _suits;

    [Export]
    private Button _add;

    [Export]
    private PackedScene _suitRow;

    private IEnumerable<QuickSuitRow> Rows => _suits.GetChildren().OfType<QuickSuitRow>();

    private bool _dice;

    /// <summary>
    /// Whether or not this is for a die, which hides some settings.
    /// </summary>
    public bool Dice
    {
        set
        {
            _dice = value;
            foreach (var row in Rows)
                row.Dice = value;
        }
    }

    public override void _Ready()
    {
        foreach (var row in Rows)
            Connect(row);
        // Adding a new row copies the last one with a modified color.
        _add.Pressed += () =>
        {
            var last = Rows.Last().Suit;
            AddRow(last with { Background = NextColor(last.Background) });
            RaiseChanged();
        };
    }

    private void AddRow(QuickSuit suit)
    {
        var row = _suitRow.Instantiate<QuickSuitRow>();
        _suits.AddChild(row);
        row.Suit = suit;
        row.Dice = _dice;
        Connect(row);
    }

    private void Connect(QuickSuitRow row)
    {
        row.Changed += RaiseChanged;
        row.Removed += () =>
        {
            RemoveRow(row);
            RaiseChanged();
        };
        ShowRemovable();
    }

    private void RemoveRow(QuickSuitRow row)
    {
        _suits.RemoveChild(row);
        row.QueueFree();
        ShowRemovable();
    }

    // The last row stays, so there's always a row to copy.
    private void ShowRemovable()
    {
        var rows = Rows.ToList();
        foreach (var row in rows)
            row.Removable = rows.Count > 1;
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
        foreach (var row in Rows.ToList())
            RemoveRow(row);
        foreach (var suit in quick.Suits.IsEmpty ? [new QuickSuit()] : quick.Suits)
            AddRow(suit);
        return true;
    }
}

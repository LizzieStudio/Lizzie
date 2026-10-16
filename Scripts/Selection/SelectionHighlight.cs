using System.Collections.Generic;
using Godot;

/// <summary>
/// Outlines controls in the colors given by <see cref="LocalSelection.SelectionColors"/>,
/// so that 2D controls can implement Lizzie selection highlighting in the player's color.
/// </summary>
public static class SelectionHighlight
{
    /// <summary>
    /// The outline's width in pixels.
    /// </summary>
    public const int OutlineWidth = 2;

    // Each theme style outlined in each selection color, shared by every control drawn with that style.
    private static readonly Dictionary<(StyleBox Style, Color Color), StyleBox> Outlined = new();

    /// <summary>
    /// Outlines a control's <paramref name="style"/> in the color of the first of <paramref name="targets"/> that's selected,
    /// or clears the outline when none is.
    /// List the most specific target first, such as a cell before its row and column.
    /// </summary>
    public static void Outline(
        Control control,
        string style,
        IReadOnlyDictionary<AtomRef, Color> colors,
        params AtomRef[] targets
    )
    {
        var color = Of(colors, targets);

        control.RemoveThemeStyleboxOverride(style);
        if (color is not Color c)
            return;

        var theme = control.GetThemeStylebox(style);
        if (!Outlined.TryGetValue((theme, c), out var outlined))
        {
            var flat = theme.Duplicate() as StyleBoxFlat ?? new StyleBoxFlat { DrawCenter = false };
            foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
                flat.SetContentMargin(side, theme.GetContentMargin(side));
            flat.BorderColor = c;
            flat.SetBorderWidthAll(OutlineWidth);
            outlined = flat;
            Outlined[(theme, c)] = outlined;
        }
        control.AddThemeStyleboxOverride(style, outlined);
    }

    private static Color? Of(IReadOnlyDictionary<AtomRef, Color> colors, params AtomRef[] targets)
    {
        foreach (var target in targets)
            if (colors.TryGetValue(target, out var color))
                return color;
        return null;
    }
}

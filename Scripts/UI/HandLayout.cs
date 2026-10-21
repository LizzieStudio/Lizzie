using System;
using System.Collections.Generic;
using System.Linq;

namespace Lizzie;

/// <summary>
/// Cards that fit sit side by side, centered.
/// When there are too many to fit, each card covers part of the one before it.
/// When a card is hovered, however, the cards shift to uncover it.
/// When dragged cards are held over the hand, a gap opens to show where they will drop.
/// </summary>
public sealed class HandLayout
{
    /// <summary>
    /// Each card's left edge.
    /// </summary>
    public IReadOnlyList<float> Lefts { get; }

    /// <summary>
    /// Each card's width.
    /// </summary>
    public IReadOnlyList<float> Widths { get; }

    private HandLayout(float[] lefts, IReadOnlyList<float> widths)
    {
        Lefts = lefts;
        Widths = widths;
    }

    /// <summary>
    /// Lays out cards of the given <paramref name="widths"/> in a row <paramref name="available"/> wide.
    /// </summary>
    /// <param name="separation">The space between cards that fit.</param>
    /// <param name="hovered">The card to show whole, or -1.</param>
    /// <param name="gapAt">The card a gap opens before, from 0 to the count, or -1 for no gap.</param>
    /// <param name="gapWidth">The width of the gap, not counting the separation around it.</param>
    public static HandLayout Place(
        IReadOnlyList<float> widths,
        float available,
        float separation,
        int hovered = -1,
        int gapAt = -1,
        float gapWidth = 0
    )
    {
        int n = widths.Count;
        var lefts = new float[n];
        if (n == 0)
            return new HandLayout(lefts, widths);

        float gap = gapAt >= 0 ? gapWidth + separation : 0;
        float natural = widths.Sum() + separation * (n - 1) + gap;

        // How far each card is from the next, except the last, scaled down to fit.
        float scale = 1;
        float start = (available - natural) / 2;
        if (natural > available)
        {
            start = 0;
            float fixedWidth = widths[^1] + gap;
            float shrinkable = 0;
            for (int i = 0; i < n - 1; i++)
                if (i == hovered)
                    fixedWidth += widths[i] + separation;
                else
                    shrinkable += widths[i] + separation;
            scale = shrinkable > 0 ? Math.Clamp((available - fixedWidth) / shrinkable, 0, 1) : 1;
        }

        float x = start;
        for (int i = 0; i < n; i++)
        {
            if (i == gapAt)
                x += gap;
            lefts[i] = x;
            x += (widths[i] + separation) * (i == hovered ? 1 : scale);
        }
        return new HandLayout(lefts, widths);
    }

    /// <summary>
    /// The card shown at <paramref name="x"/>, which is the last one covering it, or -1.
    /// </summary>
    public int CardAt(float x)
    {
        for (int i = Lefts.Count - 1; i >= 0; i--)
            if (x >= Lefts[i] && x < Lefts[i] + Widths[i])
                return i;
        return -1;
    }

    /// <summary>
    /// Where a card dropped at <paramref name="x"/> goes,
    /// computed by finding the number of cards whose middle is left of <paramref name="x"/>.
    /// </summary>
    public int InsertIndexAt(float x)
    {
        int index = 0;
        for (int i = 0; i < Lefts.Count; i++)
            if (Lefts[i] + Widths[i] / 2 < x)
                index = i + 1;
        return index;
    }
}

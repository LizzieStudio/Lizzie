using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Stacks components on the table by ZOrder. Each component rests on the highest top among the
/// components below it that it overlaps.
/// </summary>
/// <remarks>
/// Components with the same shape in the same place, like the cards of a deck, form a column.
/// Every member of a column overlaps the same things, so overlaps are found between columns
/// instead of components. Settling in ZOrder, a column's highest top so far is its top member's,
/// so a component rests on the highest of those among its own column and the columns it
/// overlaps.
/// </remarks>
internal static class Stacking
{
    /// <summary>
    /// The height each component rests at.
    /// </summary>
    /// <param name="columns">The columns, highest top first.</param>
    public static float[] Floors(
        IReadOnlyList<VisualComponentBase> components,
        out StackColumns columns
    )
    {
        // Only components with a shape stack.
        var footprints = new List<Footprint>(components.Count);
        for (int i = 0; i < components.Count; i++)
            if (components[i].ShapeProfiles.Count > 0)
                footprints.Add(new Footprint(i, components[i]));

        return Floors(components.Count, footprints.ToArray(), out columns);
    }

    /// <summary>
    /// The height each of <paramref name="count"/> components rests at, from the footprints of
    /// those with a shape.
    /// </summary>
    public static float[] Floors(int count, Footprint[] footprints, out StackColumns columns)
    {
        int n = footprints.Length;

        // Gather the footprints into columns, each led by its first footprint.
        var columnOf = new int[n];
        var leads = new int[n];
        var sizes = new int[n];
        var sameKey = new int[n];
        var byKey = new Dictionary<(int, int), int>();
        int columnCount = 0;
        for (int i = 0; i < n; i++)
        {
            ref readonly var f = ref footprints[i];
            int column = -1;
            bool pileable = Pileable(f);
            if (pileable && byKey.TryGetValue(f.Key, out int c))
                for (; c >= 0; c = sameKey[c])
                    if (SamePlacement(f, footprints[leads[c]]))
                    {
                        column = c;
                        break;
                    }

            if (column < 0)
            {
                column = columnCount++;
                leads[column] = i;
                if (pileable)
                {
                    sameKey[column] = byKey.TryGetValue(f.Key, out int next) ? next : -1;
                    byKey[f.Key] = column;
                }
            }
            columnOf[i] = column;
            sizes[column]++;
        }

        var neighbors = Neighbors(footprints, leads, columnCount, out var neighborStart);

        // Settle from the bottom up, so every column's top is its highest member's so far.
        var order = new int[n];
        var zOrders = new UInt128[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
            zOrders[i] = footprints[i].ZOrder.SortKey;
        }
        Array.Sort(zOrders, order);

        var memberStart = new int[columnCount + 1];
        for (int c = 0; c < columnCount; c++)
            memberStart[c + 1] = memberStart[c] + sizes[c];
        var members = new int[n];
        var filled = new int[columnCount];

        var floors = new float[count];
        var tops = new float[n];
        var running = new float[columnCount];
        foreach (int i in order)
        {
            int c = columnOf[i];
            float floor = running[c];
            for (int k = neighborStart[c]; k < neighborStart[c + 1]; k++)
                floor = MathF.Max(floor, running[neighbors[k]]);

            floors[footprints[i].Index] = floor;
            tops[i] = running[c] = floor + footprints[i].YHeight;
            members[memberStart[c] + filled[c]++] = i;
        }

        // Highest top first.
        var byTop = new int[columnCount];
        var sortTops = new float[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            byTop[c] = c;
            sortTops[c] = -running[c];
        }
        Array.Sort(sortTops, byTop);

        columns = new StackColumns(footprints, tops, byTop, memberStart, members);
        return floors;
    }

    /// <summary>
    /// The columns each column overlaps, found by sweeping across X to skip the ones too far
    /// apart. Column <c>c</c>'s are at [<c>start[c]</c>, <c>start[c + 1]</c>).
    /// </summary>
    private static int[] Neighbors(
        Footprint[] footprints,
        int[] leads,
        int columnCount,
        out int[] start
    )
    {
        var byX = new int[columnCount];
        var left = new float[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            byX[c] = c;
            left[c] = footprints[leads[c]].Bounds.Position.X;
        }
        Array.Sort(left, byX);

        // The bounds in X order, so the sweep reads them without touching the footprints.
        var right = new float[columnCount];
        var bottom = new float[columnCount];
        var top = new float[columnCount];
        var square = new bool[columnCount];
        for (int a = 0; a < columnCount; a++)
        {
            ref readonly var f = ref footprints[leads[byX[a]]];
            right[a] = f.Bounds.End.X;
            bottom[a] = f.Bounds.Position.Y;
            top[a] = f.Bounds.End.Y;
            square[a] = FillsBounds(f);
        }

        var pairs = new List<(int, int)>();
        for (int a = 0; a < columnCount; a++)
        for (int b = a + 1; b < columnCount && left[b] <= right[a]; b++)
        {
            if (bottom[b] > top[a] || top[b] < bottom[a])
                continue;
            int ca = byX[a],
                cb = byX[b];
            // A footprint within the bounds of a rectangle that fills them, like a board, is on it.
            bool within =
                (square[a] && right[b] <= right[a] && bottom[b] >= bottom[a] && top[b] <= top[a])
                || (
                    square[b]
                    && left[a] >= left[b]
                    && right[a] <= right[b]
                    && bottom[a] >= bottom[b]
                    && top[a] <= top[b]
                );
            ref readonly var fa = ref footprints[leads[ca]];
            ref readonly var fb = ref footprints[leads[cb]];
            if (within || fa.Key == fb.Key || ShapesOverlap(fa, fb))
                pairs.Add((ca, cb));
        }

        start = new int[columnCount + 1];
        foreach (var (a, b) in pairs)
        {
            start[a + 1]++;
            start[b + 1]++;
        }
        for (int c = 0; c < columnCount; c++)
            start[c + 1] += start[c];

        var neighbors = new int[pairs.Count * 2];
        var filled = new int[columnCount];
        foreach (var (a, b) in pairs)
        {
            neighbors[start[a] + filled[a]++] = b;
            neighbors[start[b] + filled[b]++] = a;
        }
        return neighbors;
    }

    /// <summary>
    /// Whether a footprint is one rectangle square to the table, which fills its bounds.
    /// </summary>
    private static bool FillsBounds(in Footprint f) =>
        f.Shapes is [var (shape, t, _)]
        && shape is RectangleShape2D
        && ((t.X.Y == 0 && t.Y.X == 0) || (t.X.X == 0 && t.Y.Y == 0));

    /// <summary>
    /// Whether a footprint can share a column: it's one rectangle or one circle.
    /// </summary>
    private static bool Pileable(in Footprint f) =>
        f.Shapes is [var (shape, _, _)] && shape is RectangleShape2D or CircleShape2D;

    /// <summary>
    /// Whether two pileable footprints have the same shape in the same place.
    /// </summary>
    private static bool SamePlacement(in Footprint a, in Footprint b)
    {
        var (shapeA, tA, rectA) = a.Shapes[0];
        var (shapeB, tB, rectB) = b.Shapes[0];
        return tA == tB && rectA == rectB && shapeA.GetType() == shapeB.GetType();
    }

    /// <summary>
    /// Whether two footprints overlap. Components with the same center always overlap.
    /// </summary>
    public static bool Overlaps(in Footprint a, in Footprint b) =>
        a.Bounds.Intersects(b.Bounds, includeBorders: true)
        && (a.Key == b.Key || ShapesOverlap(a, b));

    private static bool ShapesOverlap(in Footprint a, in Footprint b)
    {
        foreach (var (shapeA, tA, _) in a.Shapes)
        foreach (var (shapeB, tB, _) in b.Shapes)
            if (shapeA.Collide(tA, shapeB, tB))
                return true;

        return false;
    }
}

/// <summary>
/// The columns from a stacking pass, highest top first. A column is the components with the same
/// shape in the same place, like the cards of a deck.
/// </summary>
internal sealed class StackColumns(
    Footprint[] footprints,
    float[] tops,
    int[] byTop,
    int[] memberStart,
    int[] members
)
{
    public static readonly StackColumns Empty = new([], [], [], [0], []);

    public int Count => byTop.Length;

    /// <summary>The shape and place every member of column <paramref name="i"/> shares.</summary>
    public ref readonly Footprint Shape(int i) => ref footprints[members[memberStart[byTop[i]]]];

    /// <summary>The top of column <paramref name="i"/>, its highest member's.</summary>
    public float Top(int i) => tops[members[memberStart[byTop[i] + 1] - 1]];

    /// <summary>The members of column <paramref name="i"/> with their tops, highest first.</summary>
    public IEnumerable<(VisualComponentBase Component, float Top)> Members(int i)
    {
        for (int m = memberStart[byTop[i] + 1] - 1; m >= memberStart[byTop[i]]; m--)
            yield return (footprints[members[m]].Component, tops[members[m]]);
    }
}

/// <summary>
/// A component's footprint on the table, read once for a stacking pass.
/// </summary>
internal readonly struct Footprint
{
    public readonly int Index;
    public readonly VisualComponentBase Component;
    public readonly (int X, int Z) Key;

    /// <summary>Each of the component's shapes, placed on the table, with its own bounds.</summary>
    public readonly (Shape2D Shape, Transform2D Transform, Rect2 Rect)[] Shapes;
    public readonly Rect2 Bounds;
    public readonly ZOrder ZOrder;
    public readonly float YHeight;

    /// <summary>
    /// Where the component's record puts it, offset by <paramref name="origin"/>, if provided.
    /// </summary>
    public Footprint(int index, VisualComponentBase c, Vector3 origin = default)
    {
        var s = ComponentState.Of(c);
        var position = origin + s.PositionAt(0);

        Index = index;
        Component = c;
        Key = ComponentState.TableKey(position);
        // Zones always sit below everything else.
        ZOrder = c is VcZone ? ZOrder.Floor : s.ZOrder;
        YHeight = c.YHeight;

        var profiles = c.ShapeProfiles;
        var center = new Vector2(position.X, position.Z);
        var (sin, cos) = Mathf.SinCos(-s.Rotation.Y);
        var x = new Vector2(cos, sin);
        var y = new Vector2(-sin, cos);
        Shapes = new (Shape2D, Transform2D, Rect2)[profiles.Count];
        Bounds = default;
        for (int i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var offset = profile.Offset;
            var t = new Transform2D(x, y, center + (x * offset.X) + (y * offset.Y));
            var rect = profile.Shape.GetRect();
            Shapes[i] = (profile.Shape, t, rect);
            Bounds = i == 0 ? t * rect : Bounds.Merge(t * rect);
        }
    }
}

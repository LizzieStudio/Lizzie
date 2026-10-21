using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;
using Lizzie.AssetManagement;

namespace Lizzie;

/// <summary>
/// What one side of a printed component shows.
/// </summary>
[JsonPolymorphic]
public abstract record SideParameters
{
    /// <summary>
    /// How many cards this side shows before it repeats.
    /// </summary>
    public abstract int Count(IAtomReader R);

    /// <summary>
    /// The dataset row card <paramref name="n"/> shows, for a side drawn from a dataset.
    /// </summary>
    public virtual SnowTag Row(int n, IAtomReader R) => SnowTag.Empty;
}

/// <summary>
/// Cards drawn with <see cref="QuickTemplate"/>, or the faces of a die drawn with <see cref="DieTemplate"/>.
/// </summary>
[JsonName("QuickSide")]
public sealed record QuickSide : SideParameters
{
    public ImmutableArray<QuickSuit> Suits { get; init; } = [];

    /// <summary>
    /// Each suit's values as a Sequence Expression. Blank values make one blank card or face.
    /// </summary>
    public string[] Expressions() =>
        Suits
            .Select(suit => string.IsNullOrWhiteSpace(suit.Values) ? "\"\"" : suit.Values)
            .ToArray();

    /// <summary>
    /// Each card's suit and value, in order.
    /// </summary>
    public List<(QuickSuit Suit, string Value)> Cards() =>
        Suits
            .Zip(Expressions())
            .SelectMany(s =>
            {
                SequenceExpressions.TryParseStrings(s.Second, out var values);
                return values.Select(value => (s.First, value));
            })
            .ToList();

    public override int Count(IAtomReader R) => Cards().Count;
}

/// <summary>
/// One look shared by several Quick cards, one card per value of <see cref="Values"/>.
/// </summary>
public sealed record QuickSuit
{
    public Color Background { get; init; } = Colors.White;

    /// <summary>
    /// The color of the text and the icons.
    /// </summary>
    public Color Foreground { get; init; } = Colors.Black;

    /// <summary>
    /// A Sequence Expression.
    /// </summary>
    public string Values { get; init; } = "";

    /// <summary>
    /// The icon's name, or empty for none.
    /// </summary>
    public string Icon { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool ShowText { get; init; } = true;
}

/// <summary>
/// Frames of an image, like the cards of a scanned sheet.
/// </summary>
[JsonName("ImageSide")]
public sealed record ImageSide : SideParameters
{
    public SnowTag Image { get; init; }

    /// <summary>
    /// Which frames to show, counting from 1, as a Sequence Expression. Blank uses every frame.
    /// </summary>
    public string Frames { get; init; } = "";

    /// <summary>
    /// The frame values, fitted to <paramref name="count"/> when there is one.
    /// </summary>
    public int[] FrameValues(IAtomReader R, int? count = null)
    {
        Layout(R).TryParse(Frames, out var values, count);
        return values;
    }

    public GridLayout Layout(IAtomReader R) =>
        R.Get(ImageAsset.ById, Image)?.Layout ?? GridLayout.Single;

    public override int Count(IAtomReader R) => FrameValues(R).Length;
}

/// <summary>
/// A template drawn once per row of a dataset, or as many times as the row's quantity.
/// </summary>
[JsonName("TemplateSide")]
public sealed record TemplateSide : SideParameters
{
    public SnowTag Template { get; init; }
    public SnowTag Dataset { get; init; }

    /// <summary>
    /// The column holding how many cards each row makes, or empty for one card per row.
    /// A row whose cell isn't a whole number makes one card.
    /// </summary>
    public SnowTag Quantity { get; init; }

    public override int Count(IAtomReader R) => Rows(R).Count;

    public override SnowTag Row(int n, IAtomReader R)
    {
        var rows = Rows(R);
        return rows.Count == 0 ? SnowTag.Empty : rows[n % rows.Count];
    }

    /// <summary>
    /// Each card's row, in order.
    /// </summary>
    private List<SnowTag> Rows(IAtomReader R) =>
        R.GetRows(Dataset)
            .SelectMany(row =>
                Enumerable.Repeat(
                    row.Id,
                    int.TryParse(
                        R.Get(DataCell.ById, new CellId(row.Id, Quantity))?.Value,
                        out var n
                    )
                        ? Math.Max(n, 0)
                        : 1
                )
            )
            .ToList();
}

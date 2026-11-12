using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

namespace Lizzie;

/// <summary>
/// What one side of a printed component shows.
/// </summary>
[JsonPolymorphic]
public abstract record SideParameters
{
    /// <summary>
    /// What each component shows on this side, in order, before the side repeats.
    /// </summary>
    /// <param name="count">How many components the prototype has, which a side that stretches fills.</param>
    public abstract IReadOnlyList<FaceKey> Faces(IAtomReader R, int? count = null);
}

/// <summary>
/// Cards drawn with <see cref="QuickTemplate"/>, or the faces of a die drawn with <see cref="DieTemplate"/>.
/// </summary>
[JsonName("QuickSide")]
public sealed record QuickSide : SideParameters
{
    /// <summary>
    /// The side's suits, in order, as keys of <see cref="Suits"/>.
    /// </summary>
    public ImmutableArray<SnowTag> Order { get; init; } = [];

    /// <summary>
    /// Every suit the side has had, removed ones included, so a card showing a removed suit keeps its look.
    /// </summary>
    public WeakMap<SnowTag, QuickSuit> Suits { get; init; } = WeakMap<SnowTag, QuickSuit>.Empty;

    /// <summary>
    /// The side with <paramref name="suit"/> added after its others.
    /// </summary>
    public QuickSide Add(SnowTag id, QuickSuit suit) =>
        this with
        {
            Order = Order.Add(id),
            Suits = Suits.With(id, suit),
        };

    /// <summary>
    /// Each suit's values as a Sequence Expression. Blank values make one blank card or face.
    /// </summary>
    public string[] Expressions() =>
        Order
            .Select(id => Suits[id].Values)
            .Select(values => string.IsNullOrWhiteSpace(values) ? "\"\"" : values)
            .ToArray();

    /// <summary>
    /// Each card's face and suit, in order.
    /// </summary>
    public List<(QuickFace Face, QuickSuit Suit)> Cards() =>
        Order
            .Zip(Expressions())
            .SelectMany(s =>
            {
                SequenceExpressions.TryParseStrings(s.Second, out var values);
                return values.Select(value => (new QuickFace(s.First, value), Suits[s.First]));
            })
            .ToList();

    public override IReadOnlyList<FaceKey> Faces(IAtomReader R, int? count = null) =>
        Cards().Select(card => card.Face).ToList();
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

    /// <summary>
    /// A plain stand-in for a suit the side doesn't have.
    /// </summary>
    public static QuickSuit Placeholder(SnowTag id) =>
        new()
        {
            Background = Color.FromHsv(
                // 2 / 1 + Sqrt(5) is the golden angle
                // it's basically just an irrational number to use the id as a seed
                (float)(id * (2f / (1f + Math.Sqrt(5f))) % 1f),
                0.35f,
                0.95f
            ),
        };
}

/// <summary>
/// Frames of an image, like the cards of a scanned sheet.
/// </summary>
[JsonName("ImageSide")]
public sealed record ImageSide : SideParameters
{
    public Ref<ImageAsset> ImageRef { get; init; }

    /// <summary>
    /// Which frames to show, counting from 1, as a Sequence Expression. Blank uses every frame.
    /// </summary>
    public string Frames { get; init; } = "";

    public GridLayout Layout(IAtomReader R) =>
        R.Get(ImageAsset.ById, ImageRef)?.Layout ?? GridLayout.Single;

    public override IReadOnlyList<FaceKey> Faces(IAtomReader R, int? count = null)
    {
        Layout(R).TryParse(Frames, out var values, count);
        return values.Select(value => new FrameFace(value)).ToList();
    }
}

/// <summary>
/// A template drawn once per row of a dataset, or as many times as the row's quantity.
/// </summary>
[JsonName("TemplateSide")]
public sealed record TemplateSide : SideParameters
{
    public Ref<Template> TemplateRef { get; init; }
    public Ref<Dataset> DatasetRef { get; init; }

    /// <summary>
    /// The column holding how many cards each row makes, or None for one card per row.
    /// A row whose cell isn't a whole number makes one card.
    /// </summary>
    public Ref<DataColumn> QuantityRef { get; init; }

    public override IReadOnlyList<FaceKey> Faces(IAtomReader R, int? count = null) =>
        Rows(R).Select(row => new RowFace(row)).ToList();

    /// <summary>
    /// Each card's row, in order.
    /// </summary>
    public List<Ref<DataRow>> Rows(IAtomReader R) =>
        R.Get(DataRow.ByDataset, DatasetRef)
            .SelectMany(row =>
                Enumerable.Repeat(
                    row.Id,
                    int.TryParse(
                        R.Get(DataCell.ById, new CellId(row.Id, QuantityRef))?.Value,
                        out var n
                    )
                        ? Math.Max(n, 0)
                        : 1
                )
            )
            .ToList();
}

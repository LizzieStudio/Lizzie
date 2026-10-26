using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Lizzie;

/// <summary>
/// Utilities to create an in-memory template and dataset for quick decks.
/// </summary>
public static class QuickTemplate
{
    /// <summary>
    /// The most icons a component can show. A larger number shows one.
    /// </summary>
    /// <remarks>
    /// There's a bug in the icon code that starts at 10. Once we fix that, we can raise this.
    /// </remarks>
    public const int MaxIcons = 9;

    private const float CmPerInch = 2.54f;

    public static Template Template { get; } =
        new()
        {
            Id = Ref<Template>.Empty,
            Name = "Quick",
            Elements =
            [
                Element(
                    ("Type", "Frame"),
                    ("Width", "100"),
                    ("Height", "100"),
                    ("Stroke Width", "0"),
                    ("Background Color", "{Background}")
                ),
                Element(
                    ("Type", "Text"),
                    ("Width", "100"),
                    ("Height", "100"),
                    ("Text", "{Text}"),
                    ("Foreground", "{Foreground}"),
                    ("Autosize", "True"),
                    ("Wrap Text", "True")
                ),
                Element(
                    ("Type", "Text"),
                    ("X", "14"),
                    ("Y", "10"),
                    ("Width", "24"),
                    ("Height", "16"),
                    ("Text", "{Corner}"),
                    ("Foreground", "{Foreground}"),
                    ("Autosize", "True")
                ),
                Element(
                    ("Type", "Image"),
                    ("Y", "58"),
                    ("Width", "80"),
                    ("Height", "72"),
                    ("Image", "{Icon}"),
                    ("Foreground", "{Foreground}"),
                    ("Quantity", "{Quantity}"),
                    ("Visible", "{Icon}") // an empty image name draws the "not found" icon
                ),
            ],
        };

    /// <summary>
    /// What the <paramref name="items"/> of a printed component draw, like a side's <see cref="QuickSide.Cards"/>.
    /// </summary>
    /// <remarks>
    /// Cards that repeat, like A*2, are only drawn once.
    /// </remarks>
    public static Drawing Drawing(
        PrintedParameters p,
        IEnumerable<(QuickSuit Suit, string Value)> items
    ) =>
        new(
            Template with
            {
                Width = p.Width / 10f / CmPerInch,
                Height = p.Height / 10f / CmPerInch,
            },
            items
                .DistinctBy(c => (c.Suit.Id, c.Value))
                .Select((c, i) => (Row: new SnowTag(i + 1), Values: Values(c.Suit, c.Value)))
                .ToDictionary(r => r.Row, r => r.Values)
        );

    /// <summary>
    /// The row of <paramref name="side"/>'s <see cref="Drawing"/> that shows <paramref name="face"/>,
    /// or <see cref="SnowTag.Empty"/> when the side no longer has it.
    /// </summary>
    public static SnowTag Row(QuickSide side, QuickFace face)
    {
        int index = side.Cards()
            .Select(c => (c.Suit.Id, c.Value))
            .Distinct()
            .ToList()
            .IndexOf((face.SuitRef, face.Value));
        return index < 0 ? SnowTag.Empty : new SnowTag(index + 1);
    }

    /// <summary>
    /// What the card showing <paramref name="value"/> in <paramref name="suit"/> draws.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Values(QuickSuit suit, string value)
    {
        bool hasIcon = suit.Icon != "";
        string text = suit.ShowText ? value : "";
        int icons = Icons(value);
        return new Dictionary<string, string>
        {
            ["Background"] = suit.Background.ToHtml(),
            ["Foreground"] = suit.Foreground.ToHtml(),
            ["Text"] = hasIcon ? "" : text,
            ["Corner"] = hasIcon ? text : "",
            ["Icon"] = hasIcon && icons > 0 ? suit.Icon : "",
            ["Quantity"] = icons.ToString(),
        };
    }

    /// <summary>
    /// How many icons <paramref name="value"/> should show.
    ///
    /// <para>Parses the int, but if it's greater than <see cref="MaxIcons"/>, returns 1.</para>
    /// </summary>
    public static int Icons(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n <= MaxIcons
            ? n
            : 1;

    public static ImmutableDictionary<string, string> Element(
        params (string Name, string Value)[] parameters
    )
    {
        var element = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (name, value) in parameters)
            element[name] = value;
        return element.ToImmutable();
    }
}

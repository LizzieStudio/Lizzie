using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Godot;

/// <summary>
/// Utilities to create an in-memory template and dataset for quick cards.
/// </summary>
public static class QuickTemplate
{
    /// <summary>
    /// The most icons a card can show. A larger number shows one.
    /// </summary>
    /// <remarks>
    /// There's a bug in the icon code that starts at 10. Once we fix that, we can raise this.
    /// </remarks>
    public const int MaxIcons = 9;

    private static readonly Column Background = new() { Id = new SnowTag(1), Name = "Background" };
    private static readonly Column Foreground = new() { Id = new SnowTag(2), Name = "Foreground" };
    private static readonly Column Text = new() { Id = new SnowTag(3), Name = "Text" };
    private static readonly Column Corner = new() { Id = new SnowTag(4), Name = "Corner" };
    private static readonly Column Icon = new() { Id = new SnowTag(5), Name = "Icon" };
    private static readonly Column Quantity = new() { Id = new SnowTag(6), Name = "Quantity" };

    public static DataSet DataSet { get; } =
        new()
        {
            Id = SnowTag.Empty,
            Name = "Quick",
            Columns = [Background, Foreground, Text, Corner, Icon, Quantity],
        };

    public static Template Template { get; } =
        new()
        {
            Id = SnowTag.Empty,
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
    /// Creates the row for the card showing <paramref name="value"/> in <paramref name="suit"/>.
    /// </summary>
    public static DataRow Row(QuickSuit suit, string value)
    {
        bool hasIcon = suit.Icon != "";
        string text = suit.ShowText ? value : "";
        int icons = Icons(value);
        return new DataRow
        {
            Id = SnowTag.Empty,
            Data = new Dictionary<SnowTag, string>
            {
                [Background.Id] = suit.Background.ToHtml(),
                [Foreground.Id] = suit.Foreground.ToHtml(),
                [Text.Id] = hasIcon ? "" : text,
                [Corner.Id] = hasIcon ? text : "",
                [Icon.Id] = hasIcon && icons > 0 ? suit.Icon : "",
                [Quantity.Id] = icons.ToString(),
            }.ToImmutableDictionary(),
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

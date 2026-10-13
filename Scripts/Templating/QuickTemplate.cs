using System.Collections.Generic;
using System.Collections.Immutable;
using Godot;

/// <summary>
/// Utilities to create an in-memory template and dataset for quick cards.
/// </summary>
public static class QuickTemplate
{
    private static readonly Column Background = new() { Id = new SnowTag(1), Name = "Background" };
    private static readonly Column Foreground = new() { Id = new SnowTag(2), Name = "Foreground" };
    private static readonly Column Text = new() { Id = new SnowTag(3), Name = "Text" };
    private static readonly Column Shape = new() { Id = new SnowTag(4), Name = "Shape" };
    private static readonly Column Quantity = new() { Id = new SnowTag(5), Name = "Quantity" };

    public static DataSet DataSet { get; } =
        new()
        {
            Id = SnowTag.Empty,
            Name = "Quick",
            Columns = [Background, Foreground, Text, Shape, Quantity],
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
                    ("Type", "Image"),
                    ("Width", "80"),
                    ("Height", "80"),
                    ("Image", "{Shape}"),
                    ("Foreground", "{Foreground}"),
                    ("Quantity", "{Quantity}"),
                    ("Visible", "{Shape}") // an empty image name draws the "not found" icon
                ),
            ],
        };

    /// <summary>
    /// Creates the row for one face.
    /// </summary>
    public static DataRow Row(Color background, QuickTextureField field)
    {
        var caption = field?.Caption ?? string.Empty;
        bool isShape = field?.FaceType == TextureFactory.TextureObjectType.CoreShape;
        return new DataRow
        {
            Id = SnowTag.Empty,
            Data = new Dictionary<SnowTag, string>
            {
                [Background.Id] = background.ToHtml(),
                [Foreground.Id] = (field?.ForegroundColor ?? Colors.Black).ToHtml(),
                [Text.Id] = isShape ? string.Empty : caption,
                [Shape.Id] = isShape ? caption : string.Empty,
                [Quantity.Id] = (field?.Quantity ?? 1).ToString(),
            }.ToImmutableDictionary(),
        };
    }

    private static ImmutableDictionary<string, string> Element(
        params (string Name, string Value)[] parameters
    )
    {
        var element = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (name, value) in parameters)
            element[name] = value;
        return element.ToImmutable();
    }
}

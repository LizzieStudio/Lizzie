using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

[JsonName("Template")]
public record Template : Replicated
{
    public string Name { get; init; }

    public string SizeTemplate { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }

    /// <summary>
    /// The template's size in whole pixels at <paramref name="pixelsPerInch"/>.
    /// A die template is the size of its net (<see cref="DieTemplate.Size"/>).
    /// </summary>
    public (int Width, int Height) Pixels(float pixelsPerInch)
    {
        var (width, height) = Sides > 0 ? DieTemplate.Size(Sides) : (Width, Height);
        return (
            (int)MathF.Round(width * pixelsPerInch, MidpointRounding.AwayFromZero),
            (int)MathF.Round(height * pixelsPerInch, MidpointRounding.AwayFromZero)
        );
    }

    public ImmutableArray<ImmutableDictionary<string, string>> Elements { get; init; } =
        ImmutableArray<ImmutableDictionary<string, string>>.Empty;

    /// <summary>
    /// For dice, the number of sides. Otherwise 0 for printed templates
    /// </summary>
    [JsonIgnore]
    public int Sides => SidesOf(SizeTemplate);

    /// <summary>
    /// Parses the int out of a die-string, like 8 for "D8".
    /// </summary>
    public static int SidesOf(string sizeTemplate) =>
        sizeTemplate is ['D', .. var sides] && int.TryParse(sides, out var n) ? n : 0;

    /// <summary>
    /// Whether or not this is a die with triangle faces.
    /// </summary>
    [JsonIgnore]
    public bool TriangleFaces => Sides is 8 or 20;

    public SnowTag DataSet { get; init; }
}

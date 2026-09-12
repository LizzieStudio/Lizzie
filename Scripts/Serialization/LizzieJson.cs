using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public static class LizzieJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new GodotNativeJsonConverter<Color>(),
            new GodotNativeJsonConverter<Vector2>(),
            new CompactVector3JsonConverter(),
            new SnowportIdJsonConverter(),
            new SnowTagJsonConverter(),
        },
    };

    public static readonly JsonSerializerOptions EventOptions = new(Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };
}

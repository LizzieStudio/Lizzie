using System.Text.Json;
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
            new GodotNativeJsonConverter<Vector3>(),
            new SnowportIdJsonConverter(),
        },
    };
}

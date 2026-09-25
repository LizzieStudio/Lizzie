using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Godot;

public static class LizzieJson
{
    public static readonly JsonSerializerOptions EventOptions = new()
    {
        // set explicitly so GetTypeInfo works before the options are first used to serialize
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters =
        {
            new GodotNativeJsonConverter<Color>(),
            new GodotNativeJsonConverter<Vector2>(),
            new CompactVector3JsonConverter(),
            new SnowportIdJsonConverter(),
            new SnowTagJsonConverter(),
        },
    };
}

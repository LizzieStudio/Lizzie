using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Godot;

public static class LizzieJson
{
    public static readonly JsonSerializerOptions EventOptions = new()
    {
        // set explicitly so GetTypeInfo works before the options are first used to serialize
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { JsonNameAttribute.AddDerivedTypes },
        },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters =
        {
            new GodotNativeJsonConverter<Color>(),
            new GodotNativeJsonConverter<Vector2>(),
            new CompactVector3JsonConverter(),
            new SnowportIdJsonConverter(),
            new SnowTagJsonConverter(),
            new CommandNameJsonConverter(),
        },
    };

    /// <summary>
    /// Same as for multiplayer, but SnowTags and SnowportIds are rewritten in sequence.
    /// </summary>
    public static JsonSerializerOptions SaveOptions(
        IReadOnlyDictionary<SnowTag, SnowTag> tags,
        IReadOnlyDictionary<SnowportId, SnowportId> ids
    )
    {
        var options = new JsonSerializerOptions(EventOptions);
        options.Converters.Insert(0, new SnowTagJsonConverter(tags));
        options.Converters.Insert(0, new SnowportIdJsonConverter(ids));
        return options;
    }
}

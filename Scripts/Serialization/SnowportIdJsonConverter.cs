using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public sealed class SnowportIdJsonConverter : JsonConverter<SnowportId>
{
    public override void Write(Utf8JsonWriter writer, SnowportId id, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(id.Value);
    }

    public override SnowportId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (!reader.TryGetUInt64(out var value))
        {
            GD.PrintErr("SnowportId from json could not be parsed");
            return SnowportId.Empty;
        }
        return new SnowportId(value);
    }
}

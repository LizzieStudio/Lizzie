using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Lizzie;

public sealed class SnowStampJsonConverter : JsonConverter<SnowStamp>
{
    public override void Write(Utf8JsonWriter writer, SnowStamp id, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((long)id);
    }

    public override SnowStamp Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (!reader.TryGetInt64(out var value))
        {
            GD.PrintErr("SnowStamp from json could not be parsed");
            return SnowStamp.Empty;
        }
        return new SnowStamp(value);
    }

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        SnowStamp id,
        JsonSerializerOptions options
    )
    {
        writer.WritePropertyName(id.ToString());
    }

    public override SnowStamp ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return SnowStamp.TryParse(reader.GetString(), out var id) ? id : SnowStamp.Empty;
    }
}

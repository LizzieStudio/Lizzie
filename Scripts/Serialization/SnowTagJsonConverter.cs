using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public sealed class SnowTagJsonConverter : JsonConverter<SnowTag>
{
    public override void Write(Utf8JsonWriter writer, SnowTag tag, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(tag.Value);
    }

    public override SnowTag Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (!reader.TryGetInt32(out var value))
        {
            GD.PrintErr("SnowTag from json could not be parsed");
            return SnowTag.Empty;
        }
        return new SnowTag(value);
    }

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        SnowTag tag,
        JsonSerializerOptions options
    )
    {
        writer.WritePropertyName(tag.Value.ToString());
    }

    public override SnowTag ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return SnowTag.TryParse(reader.GetString(), out var tag) ? tag : SnowTag.Empty;
    }
}

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SnowportIdJsonConverter : JsonConverter<SnowportId>
{
    public override void Write(
        Utf8JsonWriter writer,
        SnowportId value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStringValue(value.ToString());
    }

    public override SnowportId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return SnowportId.Parse(reader.GetString());
    }
}

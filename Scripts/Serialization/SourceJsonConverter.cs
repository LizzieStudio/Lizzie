using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Writes a <see cref="Source"/> as its number, including as a dictionary key.
/// </summary>
public sealed class SourceJsonConverter : JsonConverter<Source>
{
    public override void Write(Utf8JsonWriter writer, Source source, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(source.Value);
    }

    public override Source Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return new Source(reader.GetInt32());
    }

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        Source source,
        JsonSerializerOptions options
    )
    {
        writer.WritePropertyName(source.ToString());
    }

    public override Source ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return new Source(int.Parse(reader.GetString()));
    }
}

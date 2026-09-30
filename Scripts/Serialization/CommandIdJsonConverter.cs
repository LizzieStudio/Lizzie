using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Writes a <see cref="CommandId"/> as its plain id, like "component.flip".
/// </summary>
public sealed class CommandIdJsonConverter : JsonConverter<CommandId>
{
    public override void Write(Utf8JsonWriter writer, CommandId id, JsonSerializerOptions options)
    {
        writer.WriteStringValue(id.Id);
    }

    public override CommandId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return new CommandId(reader.GetString());
    }
}

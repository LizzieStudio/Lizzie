using System;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Writes a <see cref="CommandName"/> as its plain name, like "component.flip".
/// </summary>
public sealed class CommandNameJsonConverter : JsonConverter<CommandName>
{
    public override void Write(
        Utf8JsonWriter writer,
        CommandName name,
        JsonSerializerOptions options
    )
    {
        writer.WriteStringValue(name.Value);
    }

    public override CommandName Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return new CommandName(reader.GetString());
    }
}

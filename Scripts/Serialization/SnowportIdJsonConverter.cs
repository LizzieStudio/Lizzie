using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <param name="renumber">
/// Replaces each id as it's written.
/// </param>
public sealed class SnowportIdJsonConverter(
    IReadOnlyDictionary<SnowportId, SnowportId> renumber = null
) : JsonConverter<SnowportId>
{
    public override void Write(Utf8JsonWriter writer, SnowportId id, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(Renumbered(id).Value);
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

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        SnowportId id,
        JsonSerializerOptions options
    )
    {
        writer.WritePropertyName(Renumbered(id).Value.ToString());
    }

    public override SnowportId ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return SnowportId.TryParse(reader.GetString(), out var id) ? id : SnowportId.Empty;
    }

    private SnowportId Renumbered(SnowportId id)
    {
        if (renumber == null || id == SnowportId.Empty)
            return id;
        return renumber.TryGetValue(id, out var to)
            ? to
            : throw new InvalidOperationException(
                $"SnowportId {id} was written but JsonWalker never found it, so it has no new number. "
                    + "A converter probably writes a SnowportId the walker can't see into."
            );
    }
}

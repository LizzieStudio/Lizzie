using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <param name="renumber">
/// Replaces each tag as it's written.
/// </param>
public sealed class SnowTagJsonConverter(IReadOnlyDictionary<SnowTag, SnowTag> renumber = null)
    : JsonConverter<SnowTag>
{
    public override void Write(Utf8JsonWriter writer, SnowTag tag, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(Renumbered(tag).Value);
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
        writer.WritePropertyName(Renumbered(tag).Value.ToString());
    }

    public override SnowTag ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return SnowTag.TryParse(reader.GetString(), out var tag) ? tag : SnowTag.Empty;
    }

    private SnowTag Renumbered(SnowTag tag)
    {
        if (renumber == null || tag == SnowTag.Empty)
            return tag;
        return renumber.TryGetValue(tag, out var to)
            ? to
            : throw new InvalidOperationException(
                $"SnowTag {tag} was written but JsonWalker never found it, so it has no new number. "
                    + "A converter probably writes a SnowTag the walker can't see into."
            );
    }
}

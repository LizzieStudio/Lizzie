using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// Serializes a <see cref="Vector3"/> as a compact <c>[x, y, z]</c> array instead of
/// Godot's self-describing <c>{"type":"Vector3","args":[…]}</c> form, to shrink the
/// event wire format. This is not compatible with the Godot-native encoding.
/// </summary>
public sealed class CompactVector3JsonConverter : JsonConverter<Vector3>
{
    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }

    public override Vector3 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array for Vector3");

        reader.Read();
        float x = reader.GetSingle();
        reader.Read();
        float y = reader.GetSingle();
        reader.Read();
        float z = reader.GetSingle();
        reader.Read(); // advance to EndArray

        return new Vector3(x, y, z);
    }
}

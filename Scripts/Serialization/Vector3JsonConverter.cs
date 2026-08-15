using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(Json.Stringify(Json.FromNative(value)));
    }

    public override Vector3 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return Json.ToNative(Json.ParseString(doc.RootElement.GetRawText())).As<Vector3>();
    }
}

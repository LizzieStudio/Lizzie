using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public sealed class GodotNativeJsonConverter<[MustBeVariant] T> : JsonConverter<T>
{
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(Json.Stringify(Json.FromNative(Variant.From(value))));
    }

    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return Json.ToNative(Json.ParseString(doc.RootElement.GetRawText())).As<T>();
    }
}

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Writes each <see cref="Ref{T}"/> as its <see cref="SnowTag"/>.
/// </summary>
public sealed class RefJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Ref<>);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)
            Activator.CreateInstance(
                typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments())
            );

    private sealed class Converter<T> : JsonConverter<Ref<T>>
        where T : Atom
    {
        private static JsonConverter<SnowTag> Tags(JsonSerializerOptions options) =>
            (JsonConverter<SnowTag>)options.GetConverter(typeof(SnowTag));

        public override Ref<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(Tags(options).Read(ref reader, typeof(SnowTag), options));

        public override void Write(
            Utf8JsonWriter writer,
            Ref<T> atom,
            JsonSerializerOptions options
        ) => Tags(options).Write(writer, atom.Tag, options);

        public override Ref<T> ReadAsPropertyName(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(Tags(options).ReadAsPropertyName(ref reader, typeof(SnowTag), options));

        public override void WriteAsPropertyName(
            Utf8JsonWriter writer,
            Ref<T> atom,
            JsonSerializerOptions options
        ) => Tags(options).WriteAsPropertyName(writer, atom.Tag, options);
    }
}

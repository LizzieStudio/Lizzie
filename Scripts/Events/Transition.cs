using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Marks writes that are drafts and not intended to be kept.
/// Started by <see cref="Atoms.Open(Node, System.Collections.Generic.IEnumerable{Atom})"/>.
/// </summary>
/// <param name="Start">The id of the transition's first event.</param>
[JsonConverter(typeof(Json))]
public readonly record struct Transition(SnowportId Start)
{
    /// <summary>
    /// A placeholder meaning no transition.
    /// </summary>
    public static readonly Transition None = default;

    public override string ToString() => Start.ToString();

    private sealed class Json : JsonConverter<Transition>
    {
        public override void Write(
            Utf8JsonWriter writer,
            Transition transition,
            JsonSerializerOptions options
        ) => JsonSerializer.Serialize(writer, transition.Start, options);

        public override Transition Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(JsonSerializer.Deserialize<SnowportId>(ref reader, options));
    }
}

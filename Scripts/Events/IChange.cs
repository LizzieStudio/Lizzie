using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Something an event does to atoms.
///
/// <list type="bullet">
/// <item>writes one atom's whole value, as a <see cref="Atom"/></item>
/// <item>unsets atoms, as an <see cref="Unset"/></item>
/// </list>
/// </summary>
public interface IChange;

/// <summary>
/// Reverts atoms to how they were before they were written.
///
/// <para>
/// No Query returns an unset atom, and saves leave it out.
/// Made with <see cref="AtomReaderExtensions.Unset"/> or <see cref="AtomReaderExtensions.UnsetAll"/>.
/// </para>
/// </summary>
/// <param name="Query">The type's Id, or one of its Lookups.</param>
/// <param name="Key">The id, or the lookup key.</param>
[JsonConverter(typeof(Json))]
public sealed record Unset(Query Query, object Key) : IChange
{
    internal bool Matches(Atom write) =>
        write.GetType() == Query.AtomType && Equals(Query.Of(write), Key);

    private sealed class Json : JsonConverter<Unset>
    {
        public override Unset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            var json = JsonElement.ParseValue(ref reader);
            var type = JsonNameAttribute.Find<Atom>(json.GetProperty("type").GetString());
            var query = Query.Named(type, json.GetProperty("query").GetString());
            return new(query, json.GetProperty("key").Deserialize(query.KeyType, options));
        }

        public override void Write(
            Utf8JsonWriter writer,
            Unset unset,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString("type", JsonNameAttribute.Of(unset.Query.AtomType));
            writer.WriteString("query", unset.Query.Name);
            writer.WritePropertyName("key");
            JsonSerializer.Serialize(writer, unset.Key, unset.Query.KeyType, options);
            writer.WriteEndObject();
        }
    }
}

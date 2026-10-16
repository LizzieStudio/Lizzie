using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Points at one atom by its type and id, for selection and commands.
/// The atom may be unset, or never written.
/// </summary>
/// <remarks>
/// Make one with <see cref="Id{T, I}.Ref"/> or <see cref="To"/>.
/// Read it with <see cref="IAtomReader.Get(AtomRef)"/> to get null when the atom does not exist,
/// or with <see cref="AtomReaderExtensions.Resolve"/> to get a default when the atom does not exist.
/// </remarks>
[JsonConverter(typeof(Json))]
public readonly record struct AtomRef(Type Type, object Id)
{
    /// <summary>
    /// The ref to <paramref name="atom"/>.
    /// </summary>
    public static AtomRef To(Atom atom) =>
        new(atom.GetType(), IdQuery.For(atom.GetType()).Of(atom));

    // Saved and sent as { "type": the atom type's JsonName, "id": its id }.
    private sealed class Json : JsonConverter<AtomRef>
    {
        private static readonly Dictionary<string, Type> Named = typeof(Atom)
            .Assembly.GetTypes()
            .Where(t =>
                t.IsSubclassOf(typeof(Atom)) && t.IsDefined(typeof(JsonNameAttribute), false)
            )
            .ToDictionary(t => t.GetCustomAttribute<JsonNameAttribute>(false).Name);

        public override AtomRef Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            var json = JsonElement.ParseValue(ref reader);
            var type = Named[json.GetProperty("type").GetString()];
            return new(type, json.GetProperty("id").Deserialize(IdQuery.For(type).IdType, options));
        }

        public override void Write(
            Utf8JsonWriter writer,
            AtomRef atom,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString("type", atom.Type.GetCustomAttribute<JsonNameAttribute>(false).Name);
            writer.WritePropertyName("id");
            JsonSerializer.Serialize(writer, atom.Id, IdQuery.For(atom.Type).IdType, options);
            writer.WriteEndObject();
        }
    }
}

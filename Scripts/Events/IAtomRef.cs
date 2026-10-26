using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Points at one atom, for selection and commands. The atom may be unset, or never written.
///
/// <para>
/// Every atom's id is one, through <see cref="IAtomRef{T}"/>, so a <see cref="Ref{T}"/> or a <see cref="CellId"/> converts to it on its own.
/// To refer to an atom you have, use <see cref="To"/>.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Compare two with <c>Equals</c>, not <c>==</c>, which compares the boxes rather than the ids.
/// Sets and dictionaries already use <c>Equals</c>.
/// </para>
/// <para>
/// Read it with <see cref="IAtomReader.Get(IAtomRef)"/> to get null when the atom does not exist,
/// or with <see cref="AtomReaderExtensions.Resolve"/> to get a default when the atom does not exist.
/// </para>
/// </remarks>
[JsonConverter(typeof(Json))]
public interface IAtomRef
{
    /// <summary>
    /// The type of the atom it points at.
    /// </summary>
    Type AtomType { get; }

    /// <summary>
    /// The ref to <paramref name="atom"/>.
    /// </summary>
    public static IAtomRef To(Atom atom) => (IAtomRef)IdQuery.For(atom.GetType()).Of(atom);

    // Saved and sent as { "type": the atom type's JsonName, "id": the id itself }.
    private sealed class Json : JsonConverter<IAtomRef>
    {
        private static readonly Dictionary<string, Type> Named = typeof(Atom)
            .Assembly.GetTypes()
            .Where(t =>
                t.IsSubclassOf(typeof(Atom)) && t.IsDefined(typeof(JsonNameAttribute), false)
            )
            .ToDictionary(t => t.GetCustomAttribute<JsonNameAttribute>(false).Name);

        public override IAtomRef Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            var json = JsonElement.ParseValue(ref reader);
            var type = Named[json.GetProperty("type").GetString()];
            return (IAtomRef)json.GetProperty("id").Deserialize(IdQuery.For(type).IdType, options);
        }

        public override void Write(
            Utf8JsonWriter writer,
            IAtomRef atom,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString(
                "type",
                atom.AtomType.GetCustomAttribute<JsonNameAttribute>(false).Name
            );
            writer.WritePropertyName("id");
            JsonSerializer.Serialize(writer, atom, atom.GetType(), options);
            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// An id of a <typeparamref name="T"/>.
///
/// <code>public readonly record struct CellId(Ref&lt;DataRow&gt; RowRef, Ref&lt;DataColumn&gt; ColumnRef) : IAtomRef&lt;DataCell&gt;;</code>
/// </summary>
public interface IAtomRef<T> : IAtomRef
    where T : Atom
{
    Type IAtomRef.AtomType => typeof(T);
}

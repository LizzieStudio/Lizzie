using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Writes a <see cref="WeakMap{TKey, TValue}"/> as an object of its entries.
/// Only writes keys that were kept alive by something else.
/// </summary>
public sealed class WeakMapJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(WeakMap<,>);

    public override JsonConverter CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)
            Activator.CreateInstance(
                typeof(Converter<,>).MakeGenericType(typeToConvert.GetGenericArguments())
            );

    private sealed class Converter<TKey, TValue> : JsonConverter<WeakMap<TKey, TValue>>
        where TKey : IComparable<TKey>
    {
        public override WeakMap<TKey, TValue> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) =>
            new(
                JsonSerializer.Deserialize<ImmutableSortedDictionary<TKey, TValue>>(
                    ref reader,
                    options
                )
            );

        public override void Write(
            Utf8JsonWriter writer,
            WeakMap<TKey, TValue> map,
            JsonSerializerOptions options
        )
        {
            var tags = (SnowTagJsonConverter)options.GetConverter(typeof(SnowTag));
            bool Kept(TKey key)
            {
                bool kept = true;
                JsonWalker.Visit<SnowTag>(key, tag => kept &= tags.Writes(tag));
                return kept;
            }
            JsonSerializer.Serialize(
                writer,
                map.Entries.Where(e => Kept(e.Key)).ToImmutableSortedDictionary(),
                options
            );
        }
    }
}

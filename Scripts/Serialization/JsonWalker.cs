using System;
using System.Collections;
using System.Text.Json.Serialization.Metadata;
using Lizzie.Replication.Machinery;

namespace Lizzie;

/// <summary>
/// Uses reflection to find every <typeparamref name="T"/> reachable from a serialized value,
/// like every <see cref="SnowTag"/> in an event.
/// </summary>
public static class JsonWalker
{
    /// <param name="weak">
    /// Gets each <see cref="WeakMap{TKey, TValue}"/> instead of looking inside it,
    /// for a save, which decides on its own which entries to keep. Null looks inside.
    /// </param>
    public static void Visit<T>(object value, Action<T> visit, Action<IWeakMap> weak = null)
    {
        if (value == null)
            return;

        if (value is T found)
        {
            visit(found);
            return;
        }

        // A Ref's converter writes its SnowTag.
        if (value is IRef atom)
        {
            Visit(atom.Tag, visit, weak);
            return;
        }

        // An Unset's converter writes its key.
        if (value is Unset unset)
        {
            Visit(unset.Key, visit, weak);
            return;
        }

        // A WeakMap's converter writes its entries.
        if (value is IWeakMap map)
        {
            if (weak == null)
                Visit(map.Entries, visit);
            else
                weak(map);
            return;
        }

        var info = LizzieJson.EventOptions.GetTypeInfo(value.GetType());

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                foreach (var prop in info.Properties)
                    if (prop.Get != null && MayHold(prop.PropertyType))
                        Visit(prop.Get(value), visit, weak);
                break;

            case JsonTypeInfoKind.Enumerable:
                if (MayHold(info.ElementType))
                    foreach (var item in (IEnumerable)value)
                        Visit(item, visit, weak);
                break;

            case JsonTypeInfoKind.Dictionary:
                if (value is not IDictionary dict)
                    break;
                var keys = MayHold(info.KeyType);
                var values = MayHold(info.ElementType);
                foreach (DictionaryEntry entry in dict)
                {
                    if (keys)
                        Visit(entry.Key, visit, weak);
                    if (values)
                        Visit(entry.Value, visit, weak);
                }
                break;

            // At this point a converter owns the type, like with SnowStamp, Godot vectors, etc.
        }
    }

    private static bool MayHold(Type type)
    {
        if (type == null)
            return true;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return !(
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        );
    }
}

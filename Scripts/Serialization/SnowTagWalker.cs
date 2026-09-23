using System;
using System.Collections;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Uses reflection to find every <see cref="SnowTag"/> reachable from a serialized value.
/// </summary>
public static class SnowTagWalker
{
    public static void Visit(object value, Action<SnowTag> visit)
    {
        if (value == null)
            return;

        if (value is SnowTag tag)
        {
            visit(tag);
            return;
        }

        var info = LizzieJson.EventOptions.GetTypeInfo(value.GetType());

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                foreach (var prop in info.Properties)
                    if (prop.Get != null && MayHoldTag(prop.PropertyType))
                        Visit(prop.Get(value), visit);
                break;

            case JsonTypeInfoKind.Enumerable:
                if (MayHoldTag(info.ElementType))
                    foreach (var item in (IEnumerable)value)
                        Visit(item, visit);
                break;

            case JsonTypeInfoKind.Dictionary:
                if (value is not IDictionary dict)
                    break;
                var keys = MayHoldTag(info.KeyType);
                var values = MayHoldTag(info.ElementType);
                foreach (DictionaryEntry entry in dict)
                {
                    if (keys)
                        Visit(entry.Key, visit);
                    if (values)
                        Visit(entry.Value, visit);
                }
                break;

            // If it arrives here, a converter owns the type (SnowportId, Godot vectors, primitives)
        }
    }

    /// <summary>False for declared types that can never contain a tag, to skip walking them.</summary>
    private static bool MayHoldTag(Type type)
    {
        if (type == null)
            return true;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return !(
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        );
    }
}

using System;
using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Uses reflection to find every <typeparamref name="T"/> reachable from a serialized value,
/// like every <see cref="SnowTag"/> in an event.
/// </summary>
public static class JsonWalker
{
    /// <param name="skipRecordIds">
    /// Skips the <see cref="Replicated.Id"/> of every record to find only references.
    /// </param>
    public static void Visit<T>(object value, Action<T> visit, bool skipRecordIds = false)
    {
        if (value == null)
            return;

        if (value is T found)
        {
            visit(found);
            return;
        }

        var info = LizzieJson.EventOptions.GetTypeInfo(value.GetType());

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object:
                foreach (var prop in info.Properties)
                    if (
                        prop.Get != null
                        && MayHold(prop.PropertyType)
                        && !(skipRecordIds && IsRecordId(prop))
                    )
                        Visit(prop.Get(value), visit, skipRecordIds);
                break;

            case JsonTypeInfoKind.Enumerable:
                if (MayHold(info.ElementType))
                    foreach (var item in (IEnumerable)value)
                        Visit(item, visit, skipRecordIds);
                break;

            case JsonTypeInfoKind.Dictionary:
                if (value is not IDictionary dict)
                    break;
                var keys = MayHold(info.KeyType);
                var values = MayHold(info.ElementType);
                foreach (DictionaryEntry entry in dict)
                {
                    if (keys)
                        Visit(entry.Key, visit, skipRecordIds);
                    if (values)
                        Visit(entry.Value, visit, skipRecordIds);
                }
                break;

            // At this point a converter owns the type, like with SnowportId, Godot vectors, etc.
        }
    }

    private static bool IsRecordId(JsonPropertyInfo prop) =>
        prop.AttributeProvider is PropertyInfo p
        && p.DeclaringType == typeof(Replicated)
        && p.Name == nameof(Replicated.Id);

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

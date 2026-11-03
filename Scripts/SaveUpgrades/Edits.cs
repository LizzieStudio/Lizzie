using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// Utility methods for upgrade conversions.
/// Released steps rely on these, so you can't change how these work.
/// Add a new method if you need something new.
/// </summary>
internal static class Edits
{
    /// <summary>
    /// A copy of the objects inside an array.
    /// </summary>
    public static List<JsonObject> GetObjects(JsonArray writes) =>
        writes.OfType<JsonObject>().ToList();

    /// <summary>
    /// A copy of the objects of one type inside an array.
    /// </summary>
    public static List<JsonObject> GetObjects(JsonArray writes, string type) =>
        GetObjects(writes).Where(w => (string)w["$type"] == type).ToList();

    /// <summary>
    /// <paramref name="node"/> and every object inside it, collected up front.
    /// </summary>
    public static List<JsonObject> GetObjectsRecursive(JsonNode node)
    {
        var objects = new List<JsonObject>();
        void Visit(JsonNode n)
        {
            if (n is JsonObject obj)
            {
                objects.Add(obj);
                foreach (var (_, value) in obj)
                    Visit(value);
            }
            else if (n is JsonArray array)
            {
                foreach (var item in array)
                    Visit(item);
            }
        }
        Visit(node);
        return objects;
    }

    /// <summary>
    /// A tag no atom in the save has or refers to, for a step to count up from.
    /// </summary>
    public static int NextTag(JsonArray writes)
    {
        int max = 0;
        foreach (var obj in GetObjects(writes).SelectMany(GetObjectsRecursive))
        {
            foreach (var (key, value) in obj)
            {
                bool tag = key == "id" || key.EndsWith("Ref");
                if (tag && value is JsonValue v && v.TryGetValue(out int number))
                    max = Math.Max(max, number);
            }
        }
        return max + 1;
    }

    /// <summary>
    /// The place of the item at <paramref name="index"/> in a list, three digits of the ordered characters <c>-</c> through <c>z</c>.
    /// </summary>
    public static string Place(int index)
    {
        const string digits = "-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        return $"{digits[index / 4096 % 64]}{digits[index / 64 % 64]}{digits[index % 64]}";
    }

    /// <summary>
    /// Renames a property where it is, so saves keep their order. Does nothing when it's missing.
    /// </summary>
    public static void Rename(JsonObject obj, string from, string to)
    {
        int index = obj.IndexOf(from);
        if (index < 0)
            return;
        var value = obj[from];
        obj.RemoveAt(index);
        obj.Insert(index, to, value);
    }
}

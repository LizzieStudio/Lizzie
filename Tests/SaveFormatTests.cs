using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Lizzie.Replication.Machinery;
using SaveUpgrades;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// <para>
/// Pins the format of saves and multiplayer messages, as a JSON schema of <see cref="TableEvent"/>
/// in <c>SaveFormat.schema.json</c> next to this file, titled with <see cref="SaveUpgrade.Current"/>.
/// </para>
///
/// <para>
/// Any change to a type's or property's name, or to a property's type, fails here until
/// a step is added to <c>SaveUpgrade.Steps</c> and that file is updated.
/// A change that only adds something can use an <see cref="Addition"/>.
/// </para>
/// </summary>
public class SaveFormatTests
{
    private static string Folder([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

    private static readonly string Pinned = Path.Combine(Folder(), "SaveFormat.schema.json");

    private static readonly string Received = Path.Combine(
        Folder(),
        "SaveFormat.schema.received.json"
    );

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>
    /// The schema of everything that's saved or sent, titled with its format number.
    /// Objects allow no other properties: System.Text.Json ignores unknown ones when reading,
    /// so without this a renamed property would look like an added one and the old name would pass.
    /// </summary>
    private static string Schema()
    {
        var schema = LizzieJson.EventOptions.GetJsonSchemaAsNode(
            typeof(TableEvent),
            new JsonSchemaExporterOptions
            {
                TransformSchemaNode = (_, node) =>
                {
                    if (node is JsonObject obj && obj.ContainsKey("properties"))
                        obj["additionalProperties"] = false;
                    return node;
                },
            }
        );
        schema["title"] = Title(SaveUpgrade.Current);
        return schema.ToJsonString(Indented) + "\n";
    }

    private static string Title(int format) => $"Lizzie save format {format}";

    [Fact]
    public void SaveFormatIsUnchanged()
    {
        var schema = Schema();
        var pinned = File.Exists(Pinned) ? File.ReadAllText(Pinned).ReplaceLineEndings("\n") : null;
        if (schema == pinned)
        {
            File.Delete(Received);
            return;
        }

        File.WriteAllText(Received, schema);
        if (pinned == null)
            Assert.Fail(
                $"There's no {Path.GetFileName(Pinned)} yet, so the save format isn't pinned.\n"
                    + $"The current schema is in {Path.GetFileName(Received)}; rename it to {Path.GetFileName(Pinned)}."
            );

        var changes =
            $"The save format changed: {Path.GetFileName(Received)} doesn't match {Path.GetFileName(Pinned)}.\n"
            + Changes(pinned, schema)
            + $"Compare them with:\n    code --diff Tests/{Path.GetFileName(Pinned)} Tests/{Path.GetFileName(Received)}\n";
        Assert.Fail(
            (string)JsonNode.Parse(pinned)["title"] == Title(SaveUpgrade.Current)
                ? changes
                    + "If the change is intended, add a step to SaveUpgrade.Steps.\n"
                    + "Then run this test again."
                : changes
                    + $"If the change is intended, replace {Path.GetFileName(Pinned)} with {Path.GetFileName(Received)}."
        );
    }

    /// <summary>
    /// The names removed and added between two schemas, like <c>writes[].ComponentPosition.holder</c>,
    /// leaving out those inside a type or property that was itself removed or added.
    /// </summary>
    private static string Changes(string before, string after)
    {
        var old = Names(JsonNode.Parse(before)).ToHashSet();
        var now = Names(JsonNode.Parse(after)).ToHashSet();
        string List(string heading, HashSet<string> names, HashSet<string> others)
        {
            var only = names.Where(n => !others.Contains(n)).ToList();
            var top = only.Where(n =>
                !only.Any(o => n.StartsWith(o + ".") || n.StartsWith(o + "["))
            );
            return only.Count == 0 ? "" : heading + string.Concat(top.Select(n => $"    {n}\n"));
        }
        var removed = List("Removed, so old saves that use these won't load them:\n", old, now);
        var added = List("Added:\n", now, old);
        return removed + added == ""
            ? "No names changed, so a property's type or whether it's required did.\n"
            : removed + added;
    }

    /// <summary>Every type and property name in a schema, as a path from the event.</summary>
    private static IEnumerable<string> Names(JsonNode node, string path = "event")
    {
        if (node is not JsonObject schema)
            yield break;

        if (schema["properties"] is JsonObject properties)
            foreach (var (name, property) in properties)
            {
                if (name == "$type")
                    continue;
                yield return $"{path}.{name}";
                foreach (var inner in Names(property, $"{path}.{name}"))
                    yield return inner;
            }

        if (schema["anyOf"] is JsonArray types)
            foreach (var type in types.OfType<JsonObject>())
            {
                var named = $"{path}.{type["properties"]?["$type"]?["const"]}";
                yield return named;
                foreach (var inner in Names(type, named))
                    yield return inner;
            }

        foreach (var inner in Names(schema["items"], path + "[]"))
            yield return inner;
        foreach (var inner in Names(schema["additionalProperties"], path + "{}"))
            yield return inner;
    }
}

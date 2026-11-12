using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// A component's variant became the Component atom, sharing the component's tag with its positions.
/// Variants no component uses are dropped.
/// </summary>
public sealed class ComponentAtoms : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        var components = new Dictionary<int, int>();
        foreach (var position in Edits.GetObjects(writes, "ComponentPosition"))
        {
            components.TryAdd((int)position["variantRef"], (int)position["id"]);
            position.Remove("variantRef");
        }

        foreach (var variant in Edits.GetObjects(writes, "ComponentVariant"))
        {
            if (components.TryGetValue((int)variant["id"], out int component))
            {
                variant["$type"] = "Component";
                variant["id"] = component;
            }
            else
                writes.Remove(variant);
        }
    }
}

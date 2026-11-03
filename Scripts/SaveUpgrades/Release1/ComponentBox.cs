using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// A removed component is in the box, its container, instead of being marked removed.
/// </summary>
public sealed class ComponentBox : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var position in Edits.GetObjects(writes, "ComponentPosition"))
        {
            bool removed = (bool?)position["removed"] ?? false;
            position.Remove("removed");
            if (removed)
                position["container"] = new JsonObject { ["$type"] = "InBox" };
        }
    }
}

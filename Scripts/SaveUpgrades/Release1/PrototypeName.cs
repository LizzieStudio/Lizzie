using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// A prototype's parameters no longer repeat its name, or the name of its kind.
/// </summary>
public sealed class PrototypeName : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var prototype in Edits.GetObjects(writes, "Prototype"))
        {
            var parameters = (JsonObject)prototype["parameters"];
            parameters.Remove("componentName");
            parameters.Remove("baseName");
        }
    }
}

using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// A position's container is its location, and a held component is dragged.
/// A component in the box is a bare removal instead, holding only its id and stage.
/// </summary>
public sealed class ComponentLocation : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var position in Edits.GetObjects(writes, "ComponentPosition"))
        {
            Edits.Rename(position, "container", "location");
            var location = (JsonObject)position["location"];
            switch ((string)location?["$type"])
            {
                case "Held":
                    location["$type"] = "Dragged";
                    break;
                case "InBox":
                    writes[writes.IndexOf(position)] = new JsonObject
                    {
                        ["$type"] = "ComponentPosition",
                        ["id"] = position["id"].DeepClone(),
                        ["stageRef"] = position["stageRef"].DeepClone(),
                        ["removed"] = true,
                    };
                    break;
            }
        }
    }
}

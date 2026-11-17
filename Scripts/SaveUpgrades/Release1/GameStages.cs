using System.Linq;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// The project settings no longer name the blueprint a game starts on.
/// A save without a current game gets one, started empty on that blueprint.
/// </summary>
public sealed class GameStages : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        var atoms = Edits.GetObjects(writes);
        var settings = atoms.FirstOrDefault(w => (string)w["$type"] == "ProjectGameSettings");
        if (settings?["blueprintRef"] is not { } blueprint)
            return;
        settings.Remove("blueprintRef");
        if (atoms.Any(w => (string)w["$type"] == "CurrentGame"))
            return;

        int game = Edits.NextTag(writes);
        writes.Add(
            new JsonObject
            {
                ["$type"] = "Stage",
                ["id"] = game,
                ["name"] = "Game 1",
                ["parentRef"] = (int)blueprint,
                ["rank"] = "---",
                ["isGame"] = true,
            }
        );
        writes.Add(new JsonObject { ["$type"] = "CurrentGame", ["stageRef"] = game });
    }
}

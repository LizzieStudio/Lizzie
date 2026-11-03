using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SaveUpgrades;

/// <summary>
/// Games became tables, including the default names of game stages.
/// </summary>
public sealed class TableRename : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var write in Edits.GetObjects(writes))
        {
            switch ((string)write["$type"])
            {
                case "CurrentGame":
                    write["$type"] = "CurrentTable";
                    break;
                case "Stage":
                    Edits.Rename(write, "isGame", "isTable");
                    var name = Regex.Match((string)write["name"] ?? "", @"^Game (\d+)$");
                    if ((bool?)write["isTable"] == true && name.Success)
                        write["name"] = $"Table {name.Groups[1].Value}";
                    break;
            }
        }
    }
}

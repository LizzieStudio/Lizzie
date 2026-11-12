using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// The usual step, which changes only the atoms a save writes, leaving the rest of its event alone.
/// </summary>
public abstract class WriteStep : IStep
{
    public JsonObject Run(JsonObject save)
    {
        Run((JsonArray)save["writes"]);
        return save;
    }

    /// <summary>
    /// Upgrades the atoms a save writes from the format before this step, adding and removing them in place.
    /// </summary>
    protected abstract void Run(JsonArray writes);
}

using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// A change that only added something, so saves from the format before it load as they are.
/// </summary>
/// <remarks>
/// We still need this for "write versioning".
/// When Lizzie saves a file, it may save with the addition.
/// If we didn't bump the version, a slightly older version of Lizzie would choke with no warning.
/// </remarks>
public sealed class Addition : IStep
{
    public JsonObject Run(JsonObject save) => save;
}

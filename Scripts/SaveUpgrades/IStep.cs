using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// One change to the save format, which upgrades a save from the format before it.
///
/// <para>
/// Once you create an IStep, add it to <see cref="SaveUpgrade"/>.
/// </para>
/// </summary>
public interface IStep
{
    /// <summary>
    /// The format this step upgrades a save to.
    /// </summary>
    int Format { get; }

    /// <summary>Upgrades a save's lines from the format before <see cref="Format"/>.</summary>
    void Run(List<JsonObject> lines);
}

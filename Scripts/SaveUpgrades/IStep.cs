using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// One change to the save format, which upgrades a save from the format before it.
/// </summary>
/// <remarks>
/// You usually want to use <see cref="WriteStep"/>, which sees only the atoms.
/// Only use <see cref="IStep"/> when you need to change the event, which is rare.
/// </remarks>
public interface IStep
{
    /// <summary>
    /// Upgrades a save's one event from the format before this step.
    /// </summary>
    /// <returns><paramref name="save"/>, changed, or a new event replacing it.</returns>
    JsonObject Run(JsonObject save);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// Upgrades a save written by an older version of Lizzie to the current format.
/// </summary>
public static class SaveUpgrade
{
    /// <summary>
    /// Every change to the save format, in order.
    /// For additions that don't require conversion, insert an <see cref="Addition"/>.
    /// </summary>
    private static readonly IStep[] Steps = InOrder(new Format1());

    /// <summary>
    /// The format this version writes.
    /// </summary>
    public static int Current => Steps.Length;

    /// <summary>
    /// The first line of a save.
    /// </summary>
    public static string Header => new JsonObject { ["format"] = Current }.ToJsonString();

    /// <summary>
    /// Checks that each step's format follows the one before it, starting from 1.
    /// </summary>
    public static IStep[] InOrder(params IStep[] steps)
    {
        for (int i = 0; i < steps.Length; i++)
            if (steps[i].Format != i + 1)
                throw new InvalidOperationException(
                    $"Save upgrade step {i + 1} is for format {steps[i].Format}, but should be for format {i + 1}."
                );
        return steps;
    }

    /// <summary>
    /// Takes a save's lines and upgrades them to the current format.
    /// Throws if the save is from a newer version of Lizzie.
    /// </summary>
    /// <returns>The format the save was in.</returns>
    /// <exception cref="JsonException"/>
    public static int Run(List<JsonObject> lines)
    {
        int format = 0;
        if (lines.Count > 0 && lines[0]["format"] is { } header)
        {
            format = header.GetValue<int>();
            lines.RemoveAt(0);
        }
        if (format > Current)
            throw new JsonException(
                $"The save is in format {format}, from a newer version of Lizzie. This version reads up to format {Current}."
            );

        foreach (var step in Steps.Skip(format))
            step.Run(lines);
        return format;
    }
}

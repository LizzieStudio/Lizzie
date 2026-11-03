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
    /// The converters that update the save file in order.
    ///
    /// <para>
    /// Each outer array corresponds to a "release" for users.
    /// The inner arrays are used by developers to upgrade their project files as they work.
    /// For breaking changes, derive a new <see cref="WriteStep"/> and add it.
    /// For changes that don't require conversion but won't be backwards compatible, add a new <see cref="Addition"/>.
    /// </para>
    /// </summary>
    private static readonly IStep[][] Converters = Checked([
        [
            new Format1(),
            new GameStages(),
            new TableRename(),
            new ReconciledDecks(),
            new Addition(), // an event names the view it was made in, which saves leave out
            new DatasetSpelling(),
            new RefSuffix(),
            new PrototypeName(),
            new Addition(), // an event's place names its subject, which saves leave out
            new ComponentAtoms(),
            new SeatAtoms(),
            new SnowRanks(),
            new Addition(), // an unset can be a query, and saves have no unsets
            new ComponentBox(),
            new Addition(), // an atom names the transition writing it
            new Addition(), // an event's group and end became ends, which saves leave out
            new Addition(), // a selection's player is a source, and selections aren't saved
            new Addition(), // an atom can be in several transitions, and saves hold only committed atoms
            new ComponentLocation(),
            new WeakMaps(),
        ],
    ]);

    private static IStep[][] Checked(IStep[][] converters)
    {
        if (converters.Length < Released)
        {
            throw new InvalidOperationException(
                $"""
                Release {Released} is greater than the {converters.Length} conversions.
                You're only supposed to update Released when you have new conversions.
                """
            );
        }
        else if (Released + 1 < converters.Length)
        {
            throw new InvalidOperationException(
                $"""
                There {converters.Length} conversions but the release is only {Released}.
                You're supposed to put all upcoming conversions in one release array.
                """
            );
        }
        return converters;
    }

    /// <summary>
    /// The newest version that has gone out to users.
    ///
    /// <para>
    /// This should be the only thing you need to update to release.
    /// Any <see cref="Converters"/> at this index are for an upcoming release.
    /// </para>
    /// </summary>
    private const int Released = 0;

    /// <summary>
    /// The format this version writes.
    /// </summary>
    public static SaveFormat Current =>
        Converters.Length > Released
            ? new(Converters.Length, Converters[^1].Length)
            : new(Released);

    /// <summary>
    /// Whether this version of Lizzie can read saves in a format.
    /// </summary>
    public static bool CanRead(SaveFormat format) => StepsDone(format) != null;

    /// <summary>
    /// How many steps a save in this format has been through,
    /// or null when it's from a newer version of Lizzie.
    /// </summary>
    private static int? StepsDone(SaveFormat format)
    {
        // If its release is greater than we have conversions for, it's a future version.
        if (format.Release > Converters.Length)
            return null;
        // If its release is greater and its not a draft, it's a future version.
        if (format.Release > Released && format.Draft == null)
            return null;
        // It's a full release of a current or past version.
        if (format.Draft is not int draft)
            return Converters.Take(format.Release).Sum(r => r.Length);
        // release 0 had no drafts
        if (format.Release == 0 || draft > Converters[format.Release - 1].Length)
            return null;
        // It's a draft release of a current or past version.
        return Converters.Take(format.Release - 1).Sum(r => r.Length) + draft;
    }

    /// <summary>
    /// Takes a save's lines, its header and its one event, and upgrades the event to the current format.
    /// Throws if the save is from a newer version of Lizzie.
    /// </summary>
    /// <param name="checkpoint">
    /// Called after each draft runs, with the format the lines are now in,
    /// so a developer keeps a save from each draft.
    /// </param>
    /// <returns>Whether the save needed upgrading.</returns>
    /// <exception cref="JsonException"/>
    public static bool Run(List<JsonObject> lines, Action<SaveFormat> checkpoint = null)
    {
        var format = new SaveFormat(0);
        if (lines.Count > 0 && SaveFormat.Read(lines[0]) is { } header)
        {
            format = header;
            lines.RemoveAt(0);
        }
        if (StepsDone(format) is not int done)
            throw new JsonException(
                $"The save is in format {format}, from a newer version of Lizzie. This version reads up to format {Current}."
            );
        if (lines.Count != 1)
            throw new JsonException($"A save holds one event, but this one holds {lines.Count}.");

        var steps = Converters.SelectMany(r => r).ToArray();
        int firstDraft = Converters.Take(Released).Sum(r => r.Length);
        for (int i = done; i < steps.Length; i++)
        {
            lines[0] = steps[i].Run(lines[0]);
            if (i >= firstDraft)
                checkpoint?.Invoke(new SaveFormat(Released + 1, i - firstDraft + 1));
        }
        return done < steps.Length;
    }
}

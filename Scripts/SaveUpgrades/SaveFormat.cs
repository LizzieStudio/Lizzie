using System;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SaveUpgrades;

/// <summary>
/// The version details of a save file.
/// </summary>
/// <param name="Draft">Null for a full-release save.</param>
public readonly record struct SaveFormat(int Release, int? Draft = null) : IComparable<SaveFormat>
{
    /// <summary>
    /// The first line of a save.
    /// </summary>
    public string Header
    {
        get
        {
            var header = new JsonObject { ["format"] = Release };
            if (Draft != null)
                header["draft"] = Draft;
            return header.ToJsonString();
        }
    }

    /// <summary>
    /// The format in a save's header, or null when it has none.
    /// </summary>
    public static SaveFormat? Read(JsonObject line) =>
        line["format"] is { } release
            ? new SaveFormat(release.GetValue<int>(), (int?)line["draft"])
            : null;

    /// <summary>
    /// The name of <paramref name="project"/>'s save file given this format.
    /// </summary>
    public string FileName(string project) =>
        Release > 0 ? $"{project}.v{this}.proj" : $"{project}.proj";

    /// <summary>
    /// The format a save file's name specifies, or null when it doesn't match the project.
    /// </summary>
    /// <param name="fileName">The name that the file actually has.</param>
    /// <param name="projectName">The name of the project you're trying to load.</param>
    public static SaveFormat? FromFileName(string fileName, string projectName)
    {
        // this was only used for the first every conversion
        if (fileName == $"{projectName}.proj")
            return new SaveFormat(0);
        var match = Regex.Match(fileName, @"^(.+)\.v(\d+)(?:\.(\d+))?\.proj$");
        if (
            !match.Success
            || match.Groups[1].Value != projectName
            || !int.TryParse(match.Groups[2].Value, out int release)
        )
            return null;
        if (!match.Groups[3].Success)
            return new SaveFormat(release);
        return int.TryParse(match.Groups[3].Value, out int draft)
            ? new SaveFormat(release, draft)
            : null;
    }

    public int CompareTo(SaveFormat other) =>
        (Release, Draft ?? int.MaxValue).CompareTo((other.Release, other.Draft ?? int.MaxValue));

    public override string ToString() => Draft is int draft ? $"{Release}.{draft}" : $"{Release}";
}

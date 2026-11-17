using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SaveUpgrades;

namespace Lizzie;

/// <summary>
/// A project's folder.
///
/// <para>
/// The folder holds a save for each version it was saved in, like "project.v1.20.proj".
/// The folder's name is the project's name, so renaming the folder renames the project.
/// </para>
/// </summary>
public static class ProjectFolder
{
    /// <summary>
    /// The prefix for the .proj files. "project.vX.proj"
    /// </summary>
    /// <remarks>
    /// It's the same in every project, so renaming a project only renames its folder.
    /// </remarks>
    public const string SaveName = "project";

    public static string NameOf(string folder) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static string SavePath(string folder, SaveFormat format) =>
        Path.Combine(folder, format.FileName(SaveName));

    public static List<SaveFormat> GetSaveFormats(string folder) =>
        Directory
            .EnumerateFiles(folder, "*.proj")
            .Select(file => SaveFormat.FromFileName(Path.GetFileName(file), SaveName))
            .Where(format => format != null)
            .Select(format => format.Value)
            .ToList();

    public static bool IsFileError(Exception e)
    {
        return e
            is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException;
    }

    public static bool IsPathTaken(string path) => Directory.Exists(path) || File.Exists(path);

    /// <summary>
    /// Whether <paramref name="folder"/> is somewhere inside <paramref name="parent"/>.
    /// </summary>
    /// <remarks>
    /// It ignores case, which on systems where case matters only finds more than it should.
    /// </remarks>
    public static bool IsInside(string folder, string parent) =>
        folder.StartsWith(
            Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// Whether either folder is inside the other.
    /// </summary>
    public static bool AreNested(string folder, string other) =>
        IsInside(folder, other) || IsInside(other, folder);

    /// <summary>
    /// Whether <paramref name="folder"/> can be listed as a project: it has saves,
    /// or it's empty, like a new project's. Not just any folder, since deleting a project trashes its folder.
    /// </summary>
    public static bool IsProject(string folder)
    {
        try
        {
            return GetSaveFormats(folder).Count > 0
                || !Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception e) when (IsFileError(e))
        {
            return false;
        }
    }

    public static string ProblemWithLocation(string location, string projectName)
    {
        if (ProjectName.Problem(projectName) is { } problem)
            return problem;
        if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location))
            return "Choose a folder to put it in.";
        if (IsPathTaken(Path.Combine(location, projectName)))
            return $"There's already a \"{projectName}\" there.";
        return null;
    }

    /// <exception cref="IOException">When something is already there, or the folder can't be made.</exception>
    public static string Create(string location, string name)
    {
        var folder = Normalize(Path.Combine(location, name));
        if (IsPathTaken(folder))
            throw new IOException($"There's already a \"{name}\" there.");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <exception cref="IOException">When something already has that name, or the folder can't be moved.</exception>
    public static string Rename(string folder, string name)
    {
        var renamed = Normalize(Path.Combine(Path.GetDirectoryName(folder), name));
        if (renamed == folder)
            return folder;
        // A change of case alone names the same folder on some systems, so it isn't taken.
        bool caseOnly = string.Equals(renamed, folder, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && IsPathTaken(renamed))
            throw new IOException($"There's already a \"{name}\" there.");
        Directory.Move(folder, renamed);
        return renamed;
    }

    /// <summary>
    /// Turns an old project file into a new project folder.
    /// </summary>
    public static string Adopt(string save, string location)
    {
        var name = Path.GetFileNameWithoutExtension(save);
        var folder = Path.Combine(location, name);
        for (int n = 2; IsPathTaken(folder); n++)
            folder = Path.Combine(location, $"{name} ({n})");
        Directory.CreateDirectory(folder);
        File.Move(save, SavePath(folder, new SaveFormat(0)));
        return Normalize(folder);
    }
}

/// <summary>
/// What the project manager shows about a project's folder.
/// </summary>
/// <param name="Folder">Where the project is.</param>
/// <param name="Found">False when the folder is gone, like when it was moved or its drive is unplugged.</param>
/// <param name="Readable">The newest save this version of Lizzie can read, or null.</param>
/// <param name="Newer">The newest save from a newer version of Lizzie, or null when there's none.</param>
public record ProjectStatus(string Folder, bool Found, SaveFormat? Readable, SaveFormat? Newer)
{
    public string Name => ProjectFolder.NameOf(Folder);

    public bool CanOpen => Found && (Readable != null || Newer == null);

    /// <summary>
    /// There's a version that's newer than this Lizzie version,
    /// but there's also an older, compatible version.
    /// </summary>
    public bool OpensOlderSave => Found && Readable != null && Newer != null;

    /// <summary>
    /// Reads what's in <paramref name="folder"/>.
    /// </summary>
    public static ProjectStatus Of(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return new(folder, false, null, null);
            var saves = ProjectFolder.GetSaveFormats(folder);
            var readable = saves.Where(SaveUpgrade.CanRead).Cast<SaveFormat?>().Max();
            var newer = saves.Where(f => !SaveUpgrade.CanRead(f)).Cast<SaveFormat?>().Max();
            return new(folder, true, readable, newer);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(folder, false, null, null);
        }
    }
}

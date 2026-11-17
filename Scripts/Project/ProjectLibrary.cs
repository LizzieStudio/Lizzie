using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// The projects the project manager lists, in order.
/// The list is kept in the app's config file.
/// </summary>
public static class ProjectLibrary
{
    private const string FoldersKey = "Projects";
    private const string LastOpenedKey = "LastProject";
    private const string LocationKey = "ProjectLocation";

    /// <summary>
    /// Every listed project's folder, in order.
    /// </summary>
    public static IReadOnlyList<string> Folders => Config.Registry.Get<string[]>(FoldersKey) ?? [];

    private static void SetFolders(IEnumerable<string> folders) =>
        Config.Registry.Set(FoldersKey, folders.Distinct().ToArray());

    /// <summary>
    /// The folder of the project opened last, which should open when Lizzie starts, or null.
    /// </summary>
    public static string LastOpened
    {
        get => Config.Registry.Get<string>(LastOpenedKey) is { Length: > 0 } folder ? folder : null;
        private set => Config.Registry.Set(LastOpenedKey, value ?? "");
    }

    /// <summary>
    /// Where new projects should go.
    /// </summary>
    public static string Location
    {
        get =>
            Config.Registry.Get<string>(LocationKey) is { Length: > 0 } location
                ? location
                : ProjectFolder.Normalize(
                    Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), "Lizzie Projects")
                );
        set => Config.Registry.Set(LocationKey, ProjectFolder.Normalize(value));
    }

    /// <summary>
    /// A listed project that <paramref name="folder"/> is inside of, or that's inside of it, or null.
    /// </summary>
    public static string NestedWith(string folder) =>
        Folders.FirstOrDefault(f => ProjectFolder.AreNested(folder, f));

    /// <summary>
    /// Moves <paramref name="folder"/> to the front of the list.
    /// </summary>
    public static void PutFirst(string folder) =>
        SetFolders(Folders.Where(f => f != folder).Prepend(folder));

    /// <summary>
    /// Lists <paramref name="folder"/> first and opens it when Lizzie starts.
    /// </summary>
    public static void Opened(string folder)
    {
        PutFirst(folder);
        LastOpened = folder;
    }

    /// <summary>
    /// Takes <paramref name="folder"/> off the list, but leaves the folder alone.
    /// </summary>
    public static void Remove(string folder)
    {
        SetFolders(Folders.Where(f => f != folder));
        if (LastOpened == folder)
            LastOpened = null;
    }

    /// <summary>
    /// Replaces <paramref name="priorName"/> with <paramref name="nextName"/>, but leaves the folder alone.
    /// </summary>
    public static void Renamed(string priorName, string nextName)
    {
        SetFolders(Folders.Select(f => f == priorName ? nextName : f));
        if (LastOpened == priorName)
            LastOpened = nextName;
    }

    /// <summary>
    /// Moves <paramref name="folder"/> to the system's trash and takes it off the list.
    /// </summary>
    public static Error Trash(string folder)
    {
        var error = OS.MoveToTrash(folder);
        if (error == Error.Ok)
            Remove(folder);
        return error;
    }

    /// <summary>
    /// Migrates Lizzie save files to save folders.
    /// </summary>
    /// <remarks>
    /// Very few people (if anyone) will need this, so we can probably drop it at some point.
    /// </remarks>
    public static void AdoptLooseSaves()
    {
        var saves = Directory
            .EnumerateFiles(ProjectSettings.GlobalizePath("user://"), "*.proj")
            .OrderByDescending(File.GetLastWriteTime);
        foreach (var save in saves)
        {
            try
            {
                SetFolders(Folders.Append(ProjectFolder.Adopt(save, Location)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PrintErr($"Couldn't move \"{save}\" into a project folder: {e.Message}");
            }
        }
    }
}

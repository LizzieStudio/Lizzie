using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// The rules for a project's name and folder.
/// </summary>
public static class ProjectName
{
    // A few more gotcha rules from Windows.
    // https://learn.microsoft.com/windows/win32/fileio/naming-a-file
    private static readonly HashSet<string> Reserved = new(
        new[] { "CON", "PRN", "AUX", "NUL" }.Concat(
            from device in new[] { "COM", "LPT" }
            from digit in "123456789¹²³"
            select device + digit
        ),
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Why <paramref name="name"/> can't name a project, or null when it can.
    /// </summary>
    public static string Problem(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Enter a name.";
        if (name != name.Trim())
            return "A name can't start or end with a space.";
        if (!name.IsValidFileName())
            return "A name can't contain : / \\ ? * \" | % < >";
        if (name.EndsWith('.'))
            return "A name can't end with a period.";
        if (name.Any(char.IsControl))
            return "A name can't contain tabs or other control characters.";
        if (Reserved.Contains(name.Split('.')[0].TrimEnd()))
            return $"\"{name}\" is reserved by Windows.";
        return null;
    }
}

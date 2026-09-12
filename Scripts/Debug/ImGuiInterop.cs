using System;
using ImGuiNET;

/// <summary>
/// Helpers for cooperating with the Dear ImGui debug overlay.
/// </summary>
public static class ImGuiInterop
{
    /// <summary>True when the mouse is over an ImGui window.</summary>
    public static bool ClaimingMouse =>
        ImGui.GetCurrentContext() != IntPtr.Zero && ImGui.GetIO().WantCaptureMouse;
}

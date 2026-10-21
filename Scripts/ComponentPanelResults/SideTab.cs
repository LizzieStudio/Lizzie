using System;
using Godot;

namespace Lizzie;

/// <summary>
/// One tab of a <see cref="SideEditor"/>.
/// </summary>
public abstract partial class SideTab : VBoxContainer
{
    /// <summary>
    /// Raised when the user changes the side.
    /// </summary>
    public event Action Changed;

    protected void RaiseChanged() => Changed?.Invoke();

    /// <summary>
    /// The side the controls describe.
    /// </summary>
    public abstract SideParameters Side { get; }

    /// <summary>
    /// Shows <paramref name="side"/> in the controls, returning false when it's another tab's kind.
    /// </summary>
    public abstract bool TryLoad(SideParameters side);
}

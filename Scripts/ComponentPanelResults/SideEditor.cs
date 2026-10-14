using System;
using Godot;

/// <summary>
/// Edits one side of a printed component, with a tab for each kind of side.
/// </summary>
public partial class SideEditor : TabContainer
{
    [Export]
    private SideTab[] _tabs;

    /// <summary>
    /// Invoked when the user changes the side, or picks another kind.
    /// </summary>
    public event Action Changed;

    public override void _Ready()
    {
        foreach (var tab in _tabs)
            tab.Changed += () => Changed?.Invoke();
        TabChanged += _ => Changed?.Invoke();
    }

    public SideParameters Side
    {
        get => _tabs[CurrentTab].Side;
        set
        {
            int tab = Array.FindIndex(_tabs, t => t.TryLoad(value));
            if (tab >= 0)
                CurrentTab = tab;
        }
    }
}

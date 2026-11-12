using System;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Edits one side of a printed component, or the faces of a die, with a tab for each kind of side.
/// </summary>
public partial class SideEditor : TabContainer
{
    [Export]
    private SideTab[] _tabs;

    /// <summary>
    /// Set this to true for dice.
    ///
    /// <list type="bullet">
    /// <item>hides background colors</item>
    /// <item>hides show-text checkbox</item>
    /// </list>
    /// </summary>
    [Export]
    private bool _dice;

    /// <summary>
    /// Invoked when the user changes the side, or picks another kind.
    /// </summary>
    public event Action Changed;

    public override void _Ready()
    {
        foreach (var tab in _tabs)
            tab.Changed += () => Changed?.Invoke();
        TabChanged += _ => Changed?.Invoke();
        if (_dice)
        {
            SetTabHidden(Array.FindIndex(_tabs, t => t is ImageSideTab), true);
            _tabs.OfType<QuickSideTab>().Single().Dice = true;
        }
    }

    /// <summary>
    /// Filters which templates the template tab lists.
    ///
    /// <para>Used for tokens vs dice and types of dice, like d6 vs d8.</para>
    /// </summary>
    public Func<Template, bool> ShowsTemplates
    {
        set => _tabs.OfType<TemplateSideTab>().Single().ShowsTemplates = value;
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

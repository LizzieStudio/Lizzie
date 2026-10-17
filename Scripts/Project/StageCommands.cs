using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// The commands that start games and blueprints, and reset the game.
/// </summary>
public static class StageCommands
{
    // Starting games and adding blueprints are admin events.
    private static bool HasAuthority(IAtomReader R) =>
        MultiplayerManager.Instance?.HasAuthority() != false;

    /// <summary>
    /// Puts a new, empty blueprint under the game.
    /// </summary>
    public static readonly GlobalCommand NewBlueprint = new()
    {
        Name = new("game.new_blueprint"),
        Icon = CommandIcons.NewBlueprint,
        Caption = "New Blueprint",
        Enabled = HasAuthority,
        SideEffects = _ => ProjectService.Instance.NewBlueprint(),
    };

    /// <summary>
    /// Resets the game back to setup.
    /// </summary>
    public static readonly GlobalCommand ResetGame = new()
    {
        Name = new("game.reset"),
        Keys = [Shortcuts.Ctrl(Key.R)],
        Icon = CommandIcons.ResetGame,
        Caption = "Reset Game",
        Enabled = R => Resettable(R).Any(),
        Effects = R => Resettable(R).Select(R.Unset),
    };

    // What the game wrote itself, except what someone is dragging.
    private static IEnumerable<ComponentState> Resettable(IAtomReader R) =>
        R.Get(ComponentState.WrittenIn, R.Get(CurrentGame.Only).StageRef)
            .Where(c => !R.IsBeingDragged(c));
}

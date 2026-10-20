using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Commands for blueprints and starting games on them.
/// </summary>
public static class StageCommands
{
    /// <summary>
    /// Gets whether or not <paramref name="stage"/> is a blueprint that's <see cref="Stage.Reachable"/>.
    /// </summary>
    public static bool IsBlueprint(IAtomReader R, Stage stage) =>
        !stage.IsGame && Stage.Reachable(R, stage.Id);

    /// <summary>
    /// Adds an empty blueprint last under the selected one, or at the root when none are selected.
    /// A root is named "Blueprint N", and one under a "Parent" is named "Parent-N".
    /// </summary>
    /// <remarks>
    /// This is added as a local command in the blueprint manager to avoid it showing up everywhere.
    /// A <see cref="TargetCount.Optional"/> command would do that by default.
    /// </remarks>
    public static readonly Command AddBlueprint = new AtomCommand<Stage>
    {
        Name = new("blueprint.add"),
        Icon = CommandIcons.Add,
        Caption = "Add Blueprint",
        Count = TargetCount.Optional,
        AppliesTo = IsBlueprint,
        Effects = (R, parents, _) =>
        {
            var parent = parents.FirstOrDefault();
            var siblings = R.Get(Stage.Children, parent?.Id ?? SnowTag.Empty);
            var last = siblings.Select(s => s.Rank).DefaultIfEmpty().Max();
            int n = siblings.Count + 1;
            var blueprint = new Stage
            {
                Id = Snowport.Clock.CreateTag(),
                Name = parent == null ? $"Blueprint {n}" : $"{parent.Name}-{n}",
                ParentRef = parent?.Id ?? SnowTag.Empty,
                Rank = last.After(Snowport.Clock.Create()),
            };
            return [blueprint];
        },
    };

    /// <summary>
    /// Shows the selected blueprint on the table.
    /// </summary>
    public static readonly Command EditBlueprint = new AtomCommand<Stage>
    {
        Name = new("blueprint.edit"),
        Icon = CommandIcons.Edit,
        Caption = "Edit Blueprint",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
        AppliesTo = IsBlueprint,
        SideEffects = (blueprints, _) =>
            ProjectService.Instance.GameObjects.PickedStage.Value = blueprints[0].Id,
    };

    public static readonly Command NewGame = new AtomCommand<Stage>
    {
        Name = new("blueprint.new_game"),
        Icon = CommandIcons.NewGame,
        Caption = "Start Game Here",
        Noun = ("Blueprint", "Blueprints"),
        Tooltip = "Ends the current game and starts a new one from this blueprint",
        Count = TargetCount.One,
        AppliesTo = IsBlueprint,
        Effects = (R, blueprints, _) => Stage.NewGame(R, blueprints[0].Id),
    };

    /// <summary>
    /// Deletes the selected blueprint, if nothing builds on it and new games don't start from it.
    /// </summary>
    public static readonly Command DeleteBlueprint = new AtomCommand<Stage>
    {
        Name = new("blueprint.delete"),
        Icon = CommandIcons.Delete,
        Caption = "Delete Blueprint",
        Noun = ("Blueprint", "Blueprints"),
        Keys = [Shortcuts.Key(Key.Delete)],
        Count = TargetCount.One,
        AppliesTo = IsBlueprint,
        Requires =
        [
            (
                "Can't delete the blueprint that games start on.",
                (R, s) => R.Get(ProjectGameSettings.Only).BlueprintRef != s.Id
            ),
            ("Can't delete blueprints with children.", Stage.IsLeaf),
        ],
        Effects = (R, blueprints, _) => [blueprints[0] with { Deleted = true }],
    };

    public static readonly GlobalCommand ResetGame = new()
    {
        Name = new("game.reset"),
        Keys = [Shortcuts.Ctrl(Key.R)],
        Icon = CommandIcons.ResetGame,
        Caption = "Reset Game",
        Tooltip = "Resets the game back to its blueprint.",
        Requires = [("There are no changes to reset.", (R) => Resettable(R).Any())],
        Effects = R => Resettable(R).Select(R.Unset),
    };

    // What the game wrote itself, except what someone is dragging.
    private static IEnumerable<ComponentPosition> Resettable(IAtomReader R) =>
        R.Get(ComponentPosition.WrittenIn, R.Get(CurrentGame.Only).StageRef)
            .Where(c => !R.IsBeingDragged(c));
}

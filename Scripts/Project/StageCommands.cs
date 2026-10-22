using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Commands for blueprints and starting games on them.
/// </summary>
public static class StageCommands
{
    /// <summary>
    /// Gets whether or not <paramref name="stage"/> is a blueprint rather than a game.
    /// </summary>
    public static bool IsBlueprint(IAtomReader R, Stage stage) => !stage.IsGame;

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
            var siblings = Stage.GetChildren(R, parent?.Id ?? SnowTag.Empty);
            var last = siblings.Select(s => s.Rank).DefaultIfEmpty().Max();
            int n = siblings.Count(s => !s.IsGame) + 1;
            var blueprint = new Stage
            {
                Id = SnowTag.Create(),
                Name = parent == null ? $"Blueprint {n}" : $"{parent.Name}-{n}",
                ParentRef = parent?.Id ?? SnowTag.Empty,
                Rank = last.After(SnowportId.Create()),
            };
            return [blueprint];
        },
    };

    /// <summary>
    /// Shows the selected blueprint, or the game, on the table.
    /// </summary>
    public static readonly Command EditBlueprint = new AtomCommand<Stage>
    {
        Name = new("blueprint.edit"),
        Icon = CommandIcons.Edit,
        Caption = "Show on Table",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
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

    public static readonly Command DeleteBlueprint = new AtomCommand<Stage>
    {
        Name = new("blueprint.delete"),
        Icon = CommandIcons.Delete,
        Caption = "Delete Blueprint",
        Tooltip = "Deletes the blueprints and any blueprints built on them.",
        Noun = ("Blueprint", "Blueprints"),
        Keys = [Shortcuts.Key(Key.Delete)],
        AppliesTo = IsBlueprint,
        Requires =
        [
            (
                "Can't delete blueprints that the game is on.",
                (R, s) => !Stage.IsGameOrAncestor(R, s.Id)
            ),
        ],
        Effects = (R, blueprints, _) => [blueprints[0] with { Deleted = true }],
    };

    public static readonly Command Duplicate = new AtomCommand<Stage>
    {
        Name = new("blueprint.duplicate"),
        Icon = CommandIcons.Duplicate,
        Caption = "Duplicate",
        Tooltip = "Makes a copy as a new blueprint.",
        Keys = [Shortcuts.Ctrl(Key.D)],
        Count = TargetCount.One,
        Effects = (R, stages, _) =>
        {
            var original = stages[0];
            var siblings = Stage.GetChildren(R, original.ParentRef);
            var copy = new Stage
            {
                Id = SnowTag.Create(),
                Name = $"{original.Name} Copy",
                ParentRef = original.ParentRef,
                Rank = SnowRank.At(siblings, siblings.IndexOf(original) + 1, SnowportId.Create()),
            };
            return [copy, .. Stage.Union(R, copy.Id, original.Id)];
        },
    };

    /// <summary>
    /// Moves the changes of the selected blueprint, or the game, into its parent.
    /// A blueprint is then deleted and its children move up into its place.
    /// The game is replaced with a new one, which looks the same.
    /// </summary>
    public static readonly Command MergeIntoParent = new AtomCommand<Stage>
    {
        Name = new("blueprint.merge_into_parent"),
        Icon = CommandIcons.MergeIntoParent,
        Caption = "Merge into Parent",
        Tooltip = "Merges its changes into its parent blueprint.",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
        AppliesTo = (R, s) => s.ParentRef != SnowTag.Empty && HasChanges(R, s),
        SideEffects = ShowParent,
        Effects = (R, stages, _) =>
        {
            var stage = stages[0];
            return
            [
                .. Stage.Union(R, stage.ParentRef, stage.ParentRef, stage.Id),
                .. stage.IsGame ? Stage.NewGame(R, stage.ParentRef) : Stage.Dissolve(R, stage),
            ];
        },
    };

    /// <summary>
    /// Moves the changes of the selected blueprint into each of its children, where they don't have their own,
    /// then deletes it and moves its children up into its place.
    /// </summary>
    public static readonly Command MergeIntoChildren = new AtomCommand<Stage>
    {
        Name = new("blueprint.merge_into_children"),
        Icon = CommandIcons.MergeIntoChildren,
        Caption = "Merge into Children",
        Tooltip = "Merges its changes into its children.",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
        AppliesTo = (R, s) => HasChanges(R, s) && Stage.GetChildren(R, s.Id).Count > 0,
        Requires =
        [
            (
                "Can't merge into the game, since games aren't saved.",
                (R, s) => !Stage.GetChildren(R, s.Id).Any(c => c.IsGame)
            ),
        ],
        SideEffects = ShowParent,
        Effects = (R, stages, _) =>
        {
            var stage = stages[0];
            return
            [
                .. R.Get(Stage.ChildBlueprints, stage.Id)
                    .SelectMany(c => Stage.Union(R, c.Id, stage.Id, c.Id)),
                .. Stage.Dissolve(R, stage),
            ];
        },
    };

    /// <summary>
    /// Deletes the selected blueprint, which has no changes, and moves its children up into its place.
    /// Either merge would do the same.
    /// </summary>
    public static readonly Command Dissolve = new AtomCommand<Stage>
    {
        Name = new("blueprint.dissolve"),
        Icon = CommandIcons.Dissolve,
        Caption = "Dissolve",
        Tooltip = "Removes the blueprint but keeps its children.",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
        AppliesTo = (R, s) => !HasChanges(R, s) && Stage.GetChildren(R, s.Id).Count > 0,
        Requires =
        [
            (
                "The game needs a blueprint to build on.",
                (R, s) =>
                    s.ParentRef != SnowTag.Empty || !Stage.GetChildren(R, s.Id).Any(c => c.IsGame)
            ),
        ],
        SideEffects = ShowParent,
        Effects = (R, stages, _) => Stage.Dissolve(R, stages[0]),
    };

    private static bool HasChanges(IAtomReader R, Stage stage) =>
        R.Count(ComponentPosition.WrittenIn, stage.Id) > 0;

    private static void ShowParent(IReadOnlyList<Stage> stages, int _)
    {
        var picked = ProjectService.Instance.GameObjects.PickedStage;
        if (picked.Value == stages[0].Id)
            picked.Value = stages[0].ParentRef;
    }

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

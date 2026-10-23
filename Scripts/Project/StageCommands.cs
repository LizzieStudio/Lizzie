using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Commands for blueprints and tables.
/// </summary>
public static class StageCommands
{
    // There's always a current table, so the commands that take a stage out of the tree leave it be.
    // It comes before the commands, which read it as they're made.
    private static readonly (string, AtomCommand<Stage>.AppliesToDelegate) NotTheCurrentTable = (
        "Can't remove the current table.",
        (R, s) => s.Id != R.Get(CurrentTable.Only).StageRef
    );

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
        Icon = CommandIcons.AddBox,
        Caption = "Add Blueprint",
        Tooltip = "A blueprint is a saved configuration of components.",
        Count = TargetCount.Optional,
        Effects = (R, parents, _) =>
        {
            var parent = parents.FirstOrDefault();
            var siblings = Stage.GetChildren(R, parent?.Id ?? SnowTag.Empty);
            var last = siblings.Select(s => s.Rank).DefaultIfEmpty().Max();
            int n = siblings.Count(s => !s.IsTable) + 1;
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

    public static readonly Command AddTable = new AtomCommand<Stage>
    {
        Name = new("table.add"),
        Icon = CommandIcons.AddTable,
        Caption = "Add Table",
        Tooltip = "A table is a surface that multiple people can play on.",
        Count = TargetCount.Optional,
        Effects = (R, parents, _) =>
            [Stage.NewTable(R, parents.FirstOrDefault()?.Id ?? SnowTag.Empty)],
    };

    public static readonly Command Show = new AtomCommand<Stage>
    {
        Name = new("stage.show"),
        Icon = CommandIcons.Show,
        Caption = "Show this.",
        Tooltip = "Show it in just your view.",
        Count = TargetCount.One,
        SideEffects = (blueprints, _) =>
            ProjectService.Instance.GameObjects.PickedStage.Value = blueprints[0].Id,
    };

    public static readonly Command MakeCurrent = new AtomCommand<Stage>
    {
        Name = new("table.make_current"),
        Icon = CommandIcons.MakeCurrent,
        Caption = "Play this.",
        Noun = ("Table", "Tables"),
        Tooltip = "Take all players to this table.",
        Count = TargetCount.One,
        Requires = [("Select a table, not a blueprint.", (R, s) => s.IsTable)],
        Effects = (R, tables, _) => [R.Get(CurrentTable.Only) with { StageRef = tables[0].Id }],
    };

    public static readonly Command DeleteBlueprint = new AtomCommand<Stage>
    {
        Name = new("blueprint.delete"),
        Icon = CommandIcons.Delete,
        Caption = "Delete",
        Tooltip = "Deletes it and all of its children.",
        Noun = ("Blueprint", "Blueprints"),
        Keys = [Shortcuts.Key(Key.Delete)],
        Requires =
        [
            (
                "Can't delete the current table or the blueprints it's on.",
                (R, s) => !Stage.IsCurrentTableOrAncestor(R, s.Id)
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
    /// Moves the changes of the selected stage into its parent, then deletes it and moves its children up into its place.
    /// </summary>
    public static readonly Command MergeIntoParent = new AtomCommand<Stage>
    {
        Name = new("blueprint.merge_into_parent"),
        Icon = CommandIcons.MergeIntoParent,
        Caption = "Merge into Parent",
        Tooltip = "Merges its changes into its parent.",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
        AppliesTo = (R, s) => s.ParentRef != SnowTag.Empty && HasChanges(R, s),
        Requires = [NotTheCurrentTable],
        SideEffects = ShowParent,
        Effects = (R, stages, _) =>
        {
            var stage = stages[0];
            return
            [
                .. Stage.Union(R, stage.ParentRef, stage.ParentRef, stage.Id),
                .. Stage.Dissolve(R, stage),
            ];
        },
    };

    /// <summary>
    /// Moves the changes of the selected stage into each of its children, where they don't have their own,
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
        Requires = [NotTheCurrentTable],
        SideEffects = ShowParent,
        Effects = (R, stages, _) =>
        {
            var stage = stages[0];
            return
            [
                .. R.Get(Stage.Children, stage.Id)
                    .SelectMany(c => Stage.Union(R, c.Id, stage.Id, c.Id)),
                .. Stage.Dissolve(R, stage),
            ];
        },
    };

    /// <summary>
    /// Deletes the selected stage, which has no changes, and moves its children up into its place.
    /// Either merge would do the same.
    /// </summary>
    public static readonly Command Dissolve = new AtomCommand<Stage>
    {
        Name = new("blueprint.dissolve"),
        Icon = CommandIcons.Dissolve,
        Caption = "Dissolve",
        Tooltip = "Removes it but keeps its children.",
        Noun = ("Blueprint", "Blueprints"),
        Count = TargetCount.One,
        AppliesTo = (R, s) => !HasChanges(R, s) && Stage.GetChildren(R, s.Id).Count > 0,
        Requires = [NotTheCurrentTable],
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

    public static readonly GlobalCommand ResetTable = new()
    {
        Name = new("table.reset"),
        Keys = [Shortcuts.Ctrl(Key.R)],
        Icon = CommandIcons.ResetTable,
        Caption = "Reset Table",
        Tooltip = "Resets the table back to its blueprint.",
        Requires = [("There are no changes to reset.", (R) => Resettable(R).Any())],
        Effects = R => Resettable(R).Select(R.Unset),
    };

    // What the current table wrote itself, except what someone is dragging.
    private static IEnumerable<ComponentPosition> Resettable(IAtomReader R) =>
        R.Get(ComponentPosition.WrittenIn, R.Get(CurrentTable.Only).StageRef)
            .Where(c => !R.IsBeingDragged(c));
}

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
    private static readonly (string, TargetedCommand<Stage>.AppliesToDelegate) NotTheCurrentTable =
        ("Can't remove the current table.", (R, s) => s.Id != R.Get(CurrentTable.Only).StageRef);

    /// <summary>
    /// Adds an empty blueprint last under the selected one, or at the root when none are selected.
    /// A root is named "Blueprint N", and one under a "Parent" is named "Parent-N".
    /// </summary>
    /// <remarks>
    /// This is added as a local command in the blueprint manager to avoid it showing up everywhere.
    /// A command with a <see cref="AtomCommand{T}.MinTargets"/> of 0 would do that by default.
    /// </remarks>
    [Command("blueprint.add")]
    public static readonly Command AddBlueprint = new AtomCommand<Stage>
    {
        Icon = CommandIcons.AddBlueprint,
        Caption = "Add Blueprint",
        Tooltip = "A blueprint is a saved configuration of components.",
        MinTargets = 0,
        MaxTargets = 1,
        Effects = (R, parents) =>
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

    [Command("table.add")]
    public static readonly Command AddTable = new AtomCommand<Stage>
    {
        Icon = CommandIcons.AddTable,
        Caption = "Add Table",
        Tooltip = "A table is a surface that multiple people can play on.",
        MinTargets = 0,
        MaxTargets = 1,
        Effects = (R, parents) =>
            [Stage.NewTable(R, parents.FirstOrDefault()?.Id ?? SnowTag.Empty)],
    };

    [Command("stage.show")]
    public static readonly Command Show = new AtomCommand<Stage>
    {
        Icon = CommandIcons.Show,
        Caption = "Show this.",
        Tooltip = "Show it in just your view.",
        MaxTargets = 1,
        Effects = (_, blueprints) =>
        {
            ProjectService.Instance.GameObjects.PickedStageRef.Value = blueprints[0].Id;
            return null;
        },
    };

    [Command("table.make_current")]
    public static readonly Command MakeCurrent = new AtomCommand<Stage>
    {
        Icon = CommandIcons.MakeCurrent,
        Caption = "Play this.",
        Noun = ("Table", "Tables"),
        Tooltip = "Take all players to this table.",
        MaxTargets = 1,
        Requires = [("Select a table, not a blueprint.", (R, s) => s.IsTable)],
        Effects = (R, tables) => [R.Get(CurrentTable.Only) with { StageRef = tables[0].Id }],
    };

    [Command("blueprint.delete")]
    public static readonly Command DeleteBlueprint = new AtomCommand<Stage>
    {
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
        Effects = (R, blueprints) => [blueprints[0] with { Deleted = true }],
    };

    [Command("blueprint.duplicate")]
    public static readonly Command Duplicate = new AtomCommand<Stage>
    {
        Icon = CommandIcons.Duplicate,
        Caption = "Duplicate",
        Tooltip = "Makes a copy as a new blueprint.",
        Keys = [Shortcuts.Ctrl(Key.D)],
        MaxTargets = 1,
        Effects = (R, stages) =>
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
    [Command("blueprint.merge_into_parent")]
    public static readonly Command MergeIntoParent = new AtomCommand<Stage>
    {
        Icon = CommandIcons.MergeIntoParent,
        Caption = "Merge into Parent",
        Tooltip = "Merges its changes into its parent.",
        Noun = ("Blueprint", "Blueprints"),
        MaxTargets = 1,
        AppliesTo = (R, s) => s.ParentRef != SnowTag.Empty && HasChanges(R, s),
        Requires = [NotTheCurrentTable],
        Effects = (R, stages) =>
        {
            var stage = stages[0];
            ShowParent(stage);
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
    [Command("blueprint.merge_into_children")]
    public static readonly Command MergeIntoChildren = new AtomCommand<Stage>
    {
        Icon = CommandIcons.MergeIntoChildren,
        Caption = "Merge into Children",
        Tooltip = "Merges its changes into its children.",
        Noun = ("Blueprint", "Blueprints"),
        MaxTargets = 1,
        AppliesTo = (R, s) => HasChanges(R, s) && Stage.GetChildren(R, s.Id).Count > 0,
        Requires = [NotTheCurrentTable],
        Effects = (R, stages) =>
        {
            var stage = stages[0];
            ShowParent(stage);
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
    [Command("blueprint.dissolve")]
    public static readonly Command Dissolve = new AtomCommand<Stage>
    {
        Icon = CommandIcons.Dissolve,
        Caption = "Dissolve",
        Tooltip = "Removes it but keeps its children.",
        Noun = ("Blueprint", "Blueprints"),
        MaxTargets = 1,
        AppliesTo = (R, s) => !HasChanges(R, s) && Stage.GetChildren(R, s.Id).Count > 0,
        Requires = [NotTheCurrentTable],
        Effects = (R, stages) =>
        {
            ShowParent(stages[0]);
            return Stage.Dissolve(R, stages[0]);
        },
    };

    private static bool HasChanges(IAtomReader R, Stage stage) =>
        R.Count(ComponentPosition.WrittenIn, stage.Id) > 0;

    private static void ShowParent(Stage stage)
    {
        var picked = ProjectService.Instance.GameObjects.PickedStageRef;
        if (picked.Value == stage.Id)
            picked.Value = stage.ParentRef;
    }

    [Command("table.reset")]
    public static readonly Command ResetTable = new AtomCommand<Stage>
    {
        Keys = [Shortcuts.Ctrl(Key.R)],
        Icon = CommandIcons.ResetTable,
        Caption = "Reset Table",
        Tooltip = "Resets the table back to its blueprint.",
        ActsOn = Context.Subject,
        MaxTargets = 1,
        Requires =
        [
            ("Only a table can be reset.", (R, s) => s.IsTable),
            ("There are no changes to reset.", (R, s) => Resettable(R, s).Any()),
        ],
        Effects = (R, tables) => Resettable(R, tables[0]).Select(R.Unset),
    };

    // What the table wrote itself, except what someone is dragging.
    private static IEnumerable<ComponentPosition> Resettable(IAtomReader R, Stage table) =>
        R.Get(ComponentPosition.WrittenIn, table.Id).Where(c => !R.IsBeingDragged(c));
}

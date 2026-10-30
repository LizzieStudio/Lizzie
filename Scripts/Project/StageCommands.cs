using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Commands for blueprints and tables.
/// </summary>
public static class StageCommands
{
    private static readonly (string, TargetedCommand<Stage>.AppliesToDelegate) NotTheCurrentTable =
        ("Can't remove the current table.", (R, s) => s.Id != R.Get(CurrentTable.Only).StageRef);

    private static readonly (string, TargetedCommand<Stage>.AppliesToDelegate) NotATable = (
        "You can't add to a table. Select a blueprint.",
        (R, s) => !s.IsTable
    );

    [Command("blueprint.add_child")]
    private static readonly Command AddChildBlueprint = new AtomCommand<Stage>
    {
        Icon = CommandIcons.AddBlueprint,
        Caption = "Add Blueprint",
        Tooltip = "A blueprint is a saved configuration of components.",
        MaxTargets = 1,
        Requires = [NotATable],
        Effects = (R, parents) => [Stage.NewBlueprint(R, parents[0].Id)],
    };

    [Command("blueprint.add_root")]
    private static readonly Command AddRootBlueprint = new GlobalCommand
    {
        Icon = CommandIcons.AddBlueprint,
        Caption = "Add Blueprint",
        Tooltip = "A blueprint is a saved configuration of components.",
        Effects = (R, _) => [Stage.NewBlueprint(R, Ref<Stage>.Empty)],
    };

    [Command("blueprint.add")]
    public static readonly Command AddBlueprint = new FirstOfCommand(
        AddChildBlueprint,
        AddRootBlueprint
    );

    [Command("table.add_child")]
    private static readonly Command AddChildTable = new AtomCommand<Stage>
    {
        Icon = CommandIcons.AddTable,
        Caption = "Add Table",
        Tooltip = "A table is a surface that multiple people can play on.",
        MaxTargets = 1,
        Requires = [NotATable],
        Effects = (R, parents) => [Stage.NewTable(R, parents[0].Id)],
    };

    [Command("table.add_root")]
    private static readonly Command AddRootTable = new GlobalCommand
    {
        Icon = CommandIcons.AddTable,
        Caption = "Add Table",
        Tooltip = "A table is a surface that multiple people can play on.",
        Effects = (R, _) => [Stage.NewTable(R, Ref<Stage>.Empty)],
    };

    [Command("table.add")]
    public static readonly Command AddTable = new FirstOfCommand(AddChildTable, AddRootTable);

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
            var siblings = R.Get(Stage.Children, original.ParentRef);
            var copy = new Stage
            {
                Id = Ref<Stage>.Create(),
                Name = $"{original.Name} Copy",
                ParentRef = original.ParentRef,
                Rank = SnowRank.At(siblings, siblings.IndexOf(original) + 1),
            };
            return [copy, .. Stage.Union(R, copy.Id, original.Id)];
        },
    };

    [Command("blueprint.merge_into_parent")]
    public static readonly Command MergeIntoParent = new AtomCommand<Stage>
    {
        Icon = CommandIcons.MergeIntoParent,
        Caption = "Merge into Parent",
        Tooltip = "Merges its changes into its parent.",
        Noun = ("Blueprint", "Blueprints"),
        MaxTargets = 1,
        AppliesTo = (R, s) => !s.ParentRef.IsEmpty && HasChanges(R, s),
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

    [Command("blueprint.merge_into_children")]
    public static readonly Command MergeIntoChildren = new AtomCommand<Stage>
    {
        Icon = CommandIcons.MergeIntoChildren,
        Caption = "Merge into Children",
        Tooltip = "Merges its changes into its children.",
        Noun = ("Blueprint", "Blueprints"),
        MaxTargets = 1,
        AppliesTo = (R, s) => HasChanges(R, s) && R.Count(Stage.Children, s.Id) > 0,
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

    [Command("blueprint.dissolve")]
    public static readonly Command Dissolve = new AtomCommand<Stage>
    {
        Icon = CommandIcons.Dissolve,
        Caption = "Dissolve",
        Tooltip = "Removes it but keeps its children.",
        Noun = ("Blueprint", "Blueprints"),
        MaxTargets = 1,
        AppliesTo = (R, s) => !HasChanges(R, s) && R.Count(Stage.Children, s.Id) > 0,
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
            ("There are no changes to reset.", HasChanges),
        ],
        Effects = (R, tables) => [R.UnsetAll(ComponentPosition.WrittenIn, tables[0].Id)],
    };
}

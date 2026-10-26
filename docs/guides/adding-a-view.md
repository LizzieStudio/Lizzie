# Adding a View

A view is a window or panel that the user works in, like the Dataset Editor or the Blueprints window. When you make a Node into a view, you get
* undo/redo that only reverses changes made in that view
* a selection that is synchronized to every client and shown in each user's seat color
* a context menu and keyboard shortcuts that run commands on that selection
* commands that only that view offers

## Creating a view

Implement `ICommandView` on the root Node of your window or panel.

```C#
public partial class BlueprintManager : Window, ICommandView
{
    // ...
}
```

Every Node under the view belongs to it (except Windows and their children). When one of them calls `Atoms.R.Write(this, ...)`, the write is undone and redone in that view, and when one of them has focus, keyboard shortcuts and the context menu act on that view's selection.

To open your view, make a `GlobalCommand` that instantiates its scene and adds it to the UI. See `UI.Commands.cs` for examples. Generally, you'll add this as a `CommandMenuItem` in one of the `CommandMenuButton`s of the primary scene.

## Selection

Each view has its own selection for each user. Set it with `SetSelection` and read it with `GetSelection`.

```C#
private void Select(IEnumerable<Ref<Stage>> blueprints) =>
    Atoms.R.SetSelection(Stage.ById, blueprints, this);
```

Selections are atoms, so a `Sync` that reads them reruns whenever the selection changes. Use `SelectionColors` to highlight what every user has selected in their seat color.

```C#
private void Sync(IWatchReader R)
{
    var selected = R.GetSelection(Stage.ById, this).ToHashSet();
    var colors = R.SelectionColors(ViewPlace.Of(this));
    // ...
}
```

Changing the selection is an event like any other, so Undo and Redo will bring back previous selections too.

### CanSelect

When an atom is deleted, it stays in the selection so that undoing the delete selects it again. To decide what is actually selectable, Lizzie asks the view if it `CanSelect` an atom. By default, the user can select any atom that hasn't been `Unset`, but you can narrow that down.

```C#
public bool CanSelect(IAtomReader R, IAtomRef target) => Stage.Reachable(R, (Ref<Stage>)target);
```

## Showing one atom

Some views show one atom at a time, like the Dataset Editor showing one dataset. Give these views a `Place`.

```C#
public ViewPlace? Place(IAtomReader R) =>
    R.Get(ShownDatasetRef) is var shown && !shown.IsEmpty
        ? ViewPlace.Of(this, shown)
        : null;
```

The `Place` is where the user is in the view. Each place keeps its own selection and its own undo history, so an undo in one dataset won't reverse edits to another dataset.

When `Place` returns a `ViewPlace.Of` with an atom, that atom is what commands with `ActsOn = Context.Subject` act on. This lets a command like Update Decks run on the open dataset without the user selecting it.

## View commands

To give your view commands that don't make sense anywhere else, make them `private` and list them in `Commands`. They will show up in the view's context menu and their keyboard shortcuts will work while the view has focus.

```C#
[Command("blueprint.rename")]
private static readonly Command RenameStage = new AtomCommand<Stage>
{
    Caption = "Rename",
    Keys = [Shortcuts.Key(Key.F2)],
    MaxTargets = 1,
    Effects = (_, stages) =>
    {
        EventBus.Instance.Publish(new RenameStageEvent { StageRef = stages[0].Id });
        return null;
    },
};

public IReadOnlyList<Command> Commands =>
    [StageCommands.AddBlueprint, StageCommands.AddTable, RenameStage];
```

`Commands` can also hold public commands, like `StageCommands.AddBlueprint`, to add them to this view's context menu only.

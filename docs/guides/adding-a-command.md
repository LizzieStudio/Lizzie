# Adding a Command

A command represents a labeled user action that can edit atoms and trigger side effects. Once you define a command, you can turn it into:
* A context menu item
* A menu bar item
* A dynamic button
* A keyboard shortcut

## Creating an AtomCommand

The `AtomCommand<T>` is the most common type of command, which activates when the user has selected an atom of type `T`. You initialize it on a static field with a `[Command("name")]` attribute.

```C#
[Command("some.delete")]
public static readonly Command Delete = new AtomCommand<MyAtom>
{
    Caption = "Delete",
    Effects = (R, selected) => selected.Select(a => a with { Deleted = true }),
};
```

The `Caption` displays in any menu item or button for this command.

The `Effects` delegate is run whenever the user triggers the command. It receives the `Atoms.R` and the selection of type `T`. It can return an `IEnumerable` of atoms or Unsets, which are passed to `Atoms.R.Write()`, updating the shared state, or it can return `null`, which will not trigger any event. If you return an empty collection, the event will still fire, but will not write any atoms, which is occasionally useful. You can also perform any side effects you'd like from `Effects`, like firing local events on the `EventBus`.

## Creating a GlobalCommand

The `GlobalCommand` does not work off of the selection. It shares a lot of similarities with an `AtomCommand`, but its `Effects` gets the view instead of atoms. See [Adding a View](./adding-a-view.md).

```C#
[Command("app.undo")]
public static readonly Command Undo = new GlobalCommand
{
    Caption = "Undo Mine",
    Effects = (R, view) =>
    {
        // ...
        return null;
    },
};
```

## Making it available

To make a command generally available, start by making it `public`. A `private` command only makes sense as a command local to a view (see [Adding a View](./adding-a-view.md)).

To put it in the context menu whenever an atom of type `T` is selected, add it to `CommandList.ContextMenu`.

To put it in a button, add a CommandButton node in the Godot Editor, then select your command from the "Command Name" dropdown from the inspector.

To put it in the menu bar, add a CommandMenuItem to one of the CommandMenuButtons, then select your command from the "Command Name" dropdown from the inspector.

To give the command a keyboard shortcut, set the `Keys` property.

```C#
[Command("component.delete")]
public static readonly Command Delete = new AtomCommand<ComponentPosition>
{
    // ...
    Keys = [Shortcuts.Key(Key.Delete)],
};
```

The keys will also show in menus and button tooltips. Two commands using the same keys will have those shortcuts disabled if the user's selection could activate both.

## Changing its look

There are several ways to change the information that a command presents.

```C#
[Command("dataset.update_decks")]
public static readonly Command UpdateDecks = new AtomCommand<Dataset>
{
    Caption = "Update {0}",
    Noun = ("Dataset", "Datasets"),
    Icon = CommandIcons.UpdateDecks,
    Tooltip = """
        Updates decks that show this Dataset:
        * New cards are added to the bottom of decks.
        * Updated cards are left in place.
        * Retired cards are removed.
        """,
    // ...
};
```

When you include `{0}` in the caption, it is replaced with the number of relevant selected atoms. So, with 5 datasets selected, it would be `Update 5`. If you include a `Noun` as well, it will be added to the end with the proper pluralization: `Update 5 Datasets`.

The `Icon` is the string location of an icon file. The convention is to add it to the `Textures/UI/` folder and then list it in the `CommandIcons.cs` file like `public const string Reset = Folder + "reset.svg";`. This way it's easier to reuse or update the icon.

A tooltip displays whenever the user hovers over the command in a button or menu. You are encouraged to briefly explain the command whenever it isn't self-explanatory.

## Filtering the selection

AtomCommands only receive selected atoms of the specified type `T`, but there are several more ways to filter which atoms you receive. You can:
* Only receive the atoms selected directly or indirectly
* Filter the atoms with a function
* Filter the atoms with a set of rules
* Only activate when a minimum or maximum number is selected

### ActsOn and Context

When activated, a command acts on the user's selection or atoms related to that selection. To specify which part of the selection you care about, initialize the `ActsOn` property to any combination of `Context` flags.

```C#
ActsOn = Context.Selected | Context.Contents | Context.Containers | Context.Referenced | Context.Subject,
```

The flags mean the following:
* Selected--The actual highlighted selection.
* Contents--The atoms contained within the selection, like a deck's cards.
* Containers--The atoms that contain the selection, like a card's deck.
* Referenced--The atoms that the selection references, like a component's prototype.
* Subject--The atom that is open in the view where the context menu was triggered, like the dataset in its editor.

When a button or menu item is triggered, the command will receive the atoms relative to the selection as per its `ActsOn` property. So if your component command is marked with `ActsOn = Context.Contents`, the command will receive cards whenever a deck is selected, but won't receive cards whenever cards are selected.

Keyboard shortcuts will only act on Selected, Contents, and Subject, though.

### AppliesTo

An `AtomCommand<T>` already only applies to atoms of type `T`, but if you want to filter the selection even further, you can initialize the `AppliesTo` function.

```C#
[Command("die.roll")]
public static readonly Command Roll = new AtomCommand<ComponentPosition>
{
    // ...
    AppliesTo = (R, c) => R.Kind(c) == VisualComponentType.Die,
};
```

`AppliesTo` receives `Atoms.R` and each individual atom of type `T`, but only if they pass the `ActsOn` filter.

### Requires

`AppliesTo` silently filters the selection, but if you want to give the user a description of why the command doesn't apply to the selection, you need to give `Requires` a rule.

```C#
[Command("table.reset")]
public static readonly Command ResetTable = new AtomCommand<Stage>
{
    // ...
    Requires = [("Only a table can be reset.", (R, s) => s.IsTable)],
};
```

When a command is disabled due to a `Requires` rule, the user is shown the rule text in a tooltip.

### MinTargets and MaxTargets

If the command only makes sense with a certain number of targets, you can specify the `MinTargets` and `MaxTargets`. By default, the minimum is 1 and the maximum is (effectively) unlimited. The user will only be able to activate the command if the count of selected atoms of type `T` that get through `ActsOn`, `AppliesTo`, and `Requires` is inside of `MinTargets` and `MaxTargets`.

The most common use case is to set `MaxTargets = 1`, like with the Edit Prototype command. Note that if you set `MinTargets = 0` and include it in `CommandList.ContextMenu`, it will show up in every context menu.
# Adding an Atom

Atoms are the synchronized state of a Lizzie game. When you implement an atom, you get
* an object which is synchronized to every client
* undo/redo for the user
* an object which is saved with the project
* an observable API for reactive user interfaces

## Parts of an Atom

The simplest atom inherits from `Atom` and has a `[JsonName("name")]`, an `Id`, and a `ById` query.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    public required SnowTag Id { get; init; }

    public static readonly Identity<MyAtom, SnowTag> ById = new(r => r.Id);
}
```

The `JsonName("MyAtom")` attribute sets the save file name. The convention is to use the classname.

The `SnowTag Id` could be any type and name you want, but `SnowTag Id` is the convention. You get a new SnowTag by calling `SnowTag.Create()`.

The `Identity<MyAtom, SnowTag> ById = new(r => r.Id)` is a query which every atom needs. It tells Lizzie how to get this atom and differentiate it from other atoms.

Importantly, atoms should be made immutable by using `{ get; init; }` properties. Atoms are kept in an event-log so that the user can undo and redo changes, so you can't update an atom once it's written.

## Reading and Writing

Write atoms with `AtomService.Instance.Write(myAtom, myAtom2, ...)`. It will then be available on all clients with `AtomService.Instance.Get(MyAtom.ById, itsId)`.

To update an atom, write a new one with the same type and Id. Records make this easy by giving you a `with` operator.

```C#
var myAtom = AtomService.Instance.Get(MyAtom.ById, itsId);
AtomService.Instance.Write(myAtom with { someProp = true });
```

To make this more convenient, you can use `Update`.

```C#
AtomService.Instance.Update(
    MyAtom.ById,
    itsId,
    myAtom => myAtom with { someProp = true }
);
```

##### The All query

The `All` query gives clients access to all atoms of a type with `AtomService.Instance.Get(MyAtom.All)`.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    // ...
    public static readonly All<MyAtom> MyAtoms = new();
}
```

You can optionally add a filter to an `All` query: `All<MyAtom> MyAtoms = new(a => a.Showing)`. You can have as multiple of these queries on an atom.

##### The Lookup query

A `Lookup<>` query partitions all atoms of a type into keyed groups which can be retreived by `AtomService.Instance.Get(MyAtom.ByParent, parentId)`. Return `null` if you don't want the atom to show up under any key.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    // ...
    public SnowTag? ParentRef { get; init; } = null;
    public static readonly Lookup<MyAtom, SnowTag> ByParent = new((a) => a.ParentRef);
}
```

You can have multiple of these queries on an atom as well.

##### The Singlton query

If you only need one instance of an atom type per game, replace the `Identity` query with a `Singleton` query, then retreive it with `AtomService.Instance.Get(MyAtom.Only)`.

```C#
[JsonName("ProjectGameSettings")]
public sealed record ProjectGameSettings : Atom
{
    public static readonly Singleton<ProjectGameSettings> Only = new();
    // ...
}
```

Any write to a `Singleton` atom will overwrite the one shared atom.

##### The standard structure

When two atoms share a relationship, only give one of them a SnowTag reference to the other. This ensures there is only one "source of truth" for the relationship, preventing errors.

The conventional structure is to have "root" atoms with an `All` query, like the DataSets, then "children" atoms that reference the roots by `Lookup` queries, like the DataRows and DataColumns.

## Undo/Redo

Atom updates that you `Write` together are undone together, so you should write related changes as one `Write` call: `AtomService.Instance.Write(A, B, C, ...)`.

##### Undo Groups

You can combine multiple writes into one undo group with `Open`, `Append`, and `Close`.

```C#
var groupId = AtomService.Instance.Open(myAtom);
// ...
AtomService.Instance.Append(groupId, someOtherAtom);
// ...
AtomService.Instance.Append(groupId, someThirdAtom);
// ...
AtomService.Instance.Close(groupId, someThirdAtom);
```

While an undo group is "open", it cannot be Undone or Redone. Once it's "closed", Undo and Redo events will reverse or bring back all of the writes as a unit. This is useful for "gestures", like dragging components, or for repeated edits to a focused input like a number picker.

You can also Cancel the group with `CancelIfOpen`. When you cancel a group, all of its writes are reversed as if they never happened.

```C#
// no writes because the group is going away
AtomService.Instance.CancelIfOpen(groupId);
```

## Saving

When the user saves the project, Lizzie spiders through the atoms checking to see which are accessible from the queries, and any that can't be reached are discarded. There are a few things to remember:
* Use `SnowTags` when possible, and don't convert them to numbers or strings.
* Don't add queries you aren't going to use, especially an `All` query.
* Keep atoms out of `Lookup` by returning null.

##### NotSaved

To prevent an atom from being saved, give it the `NotSaved` attribute. Even if it is accessible, it will not be saved.

```C#
[NotSaved]
[JsonName("Selection")]
public record Selection : Atom
{
    // ...
}
```

## The Reactive API

You'll often need to keep Godot Nodes in sync with the atoms. To subscribe to changes in the atoms, call `AtomService.Instance.Watch(myNode, Sync)` inside of a node's `_EnterTree`.

```C#
public override void _EnterTree()
{
    AtomService.Instance.Watch(this, Sync);
}

private void Sync(IWatchReader R)
{
    _myAtomDropdown.Clear();
    foreach (var atom in R.Get(MyAtom.All))
        _myAtomDropdown.AddItem(atom.Name, atom.Id);
}
```

The `Sync` delegate won't actually run every time *any* atom changes. Instead, `Watch` will schedule the `Sync` to run once at the end of the current frame, at which point `Sync` will subscribe to any changes in atoms that it reads through `IWatchReader R`, which has the same interface for reading records as `AtomService.Instance`. Whenever any atom that was accessed is updated, the `Sync` will be rerun, and its subscriptions will be regenerated from that run.

##### Diff

To create and remove nodes in response to atom changes, use `R.Diff(MyAtom.Query)`. `Diff` returns lists of atoms added and removed since the last `Sync`. On the first `Sync`, however, all atoms are returned as "added".

```C#
public class MyAtomManager : Panel
{
    public override void _EnterTree()
    {
        AtomService.Instance.Watch(this, Sync);
    }

    private void Sync(IWatchReader R)
    {
        var (removed, added) = R.Diff(MyAtom.All);

        foreach (var r in removed)
            RemoveAtomChild(r.Id);
        foreach (var a in added)
            AddAtomChild(a.Id);
    }
}
```

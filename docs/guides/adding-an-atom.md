# Adding an Atom

Atoms are the synchronized state of a Lizzie project. When you implement an atom, you get
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
    public required Ref<MyAtom> Id { get; init; }

    public static readonly Identity<MyAtom, Ref<MyAtom>> ById = new(r => r.Id);
}
```

The `JsonName("MyAtom")` attribute sets the type's name in the save file.

The `Ref<MyAtom> Id` keeps track of the atom's identity during serialization. You can create a new one with `Ref<MyAtom>.Create()`. Pass it out through `Identity<MyAtom, Ref<MyAtom>> ById = new(r => r.Id)` so Lizzie knows how to store this atom.

Atoms need to be immutable by making them records that use `{ get; init; }` properties. Lizzie keeps atoms in the Timeline to facilitate undo/redo, so you can't change an atom's state once created.

## Reading and Writing

Write atoms from a Godot Node with `Atoms.R.Write(this, myAtom, myAtom2, ...)`, where `this` is the node. It will then be available on all clients with `Atoms.R.Get(MyAtom.ById, itsId)`. To update an atom, write a new one with the same type and Id. Records make this easy by giving you a `with` operator.

```C#
var myAtom = Atoms.R.Get(MyAtom.ById, itsId);
Atoms.R.Write(this, myAtom with { someProp = true });
```

To make this more convenient, you can use `Update`.

```C#
Atoms.R.Update(this, MyAtom.ById, itsId,
    myAtom => myAtom with { someProp = true }
);
```

To delete an atom, write an `R.Unset(myAtom)`. It will be reset to before it was first written.

```C#
var myAtom = Atoms.R.Get(MyAtom.ById, itsId);
Atoms.R.Write(this, Atoms.R.Unset(myAtom));
```

### The All query

The `All` query gives clients access to all atoms of a type with `Atoms.R.Get(MyAtom.MyAtoms)`.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    // ...
    public static readonly All<MyAtom> MyAtoms = new();
}
```

You can optionally add a filter to an `All` query: `All<MyAtom> MyAtoms = new(a => a.Showing)`. You can have multiple of these queries on an atom.

### The Lookup query

A `Lookup<>` query partitions all atoms of a type into keyed groups which you retrieve with `Atoms.R.Get(MyAtom.ByParent, parentId)`. Return `null` if you don't want the atom to show up under any key.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    // ...
    public Ref<MyAtom>? ParentRef { get; init; } = null;
    public static readonly Lookup<MyAtom, Ref<MyAtom>?> ByParent = new((a) => a.ParentRef);
}
```

You can have multiple of these queries on an atom as well.

### The Singleton query

If you only need one instance of an atom type per project, replace the `Identity` query with a `Singleton Only` query, then retrieve it with `Atoms.R.Get(MyAtom.Only)`.

```C#
[JsonName("ProjectGameSettings")]
public sealed record ProjectGameSettings : Atom
{
    public static readonly Singleton<ProjectGameSettings> Only = new();
    // ...
}
```

Any write to a `Singleton` atom will overwrite the one shared atom.

### The standard structure

When two atoms share a relationship, only give one of them a `Ref<>` to the other. This ensures there is only one "source of truth" for the relationship, preventing errors. The other atom can then have a `Lookup` to find the atoms that reference it.

A common structure for atoms is to have "root" atoms with an `All` query, like the Datasets, then "children" atoms that reference the roots by `Lookup` queries, like the DataRows and DataColumns.

## Undo/Redo

Atom updates that you `Write` together are undone together, so you should write related changes as one `Write` call: `Atoms.R.Write(this, A, B, C, ...)`. Lizzie finds the `ICommandView` that holds the Node `this` and assigns this event to it. Undo & Redo will only reverse events made in an `ICommandView` when it has focus, so an undo in the Dataset Editor won't reverse moving a card on the table and vice versa.

### Undo Groups

You can combine multiple writes into one undo group with `Open`, `Append`, and `Close`.

```C#
var groupId = Atoms.R.Open(this, myAtom);
// ...
Atoms.R.Append(groupId, someOtherAtom);
// ...
Atoms.R.Append(groupId, someThirdAtom);
// ...
Atoms.R.Close(groupId, someFourthAtom);
```

While an undo group is "open", it cannot be Undone or Redone. Once it's "closed", Undo and Redo events will reverse or bring back all of the writes as a unit. This is useful for "gestures", like dragging components, or for repeated edits to a focused input like a number picker.

You can also Cancel the group with `CancelIfOpen`. When you cancel a group, all of its writes are reversed as if they never happened.

```C#
// no writes because the group is going away
Atoms.R.CancelIfOpen(groupId);
```

## Saving

When the user saves the project, Lizzie spiders through the atoms checking to see which are accessible from the queries, and any that can't be reached are discarded. There are a few things to remember:
* Use `Ref<AtomType>` when possible, and don't convert them to numbers or strings.
* Don't add queries you aren't going to use, especially an `All` query.
* Keep atoms out of `Lookup` by returning null.
* Don't create a `Lookup` with primitive keys unless you want to keep those atoms.

### NotSaved

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

You'll often need to keep Godot Nodes in sync with the atoms. To subscribe to changes in the atoms, call `Atoms.R.Watch(myNode, Sync)` inside of a node's `_EnterTree`.

```C#
public override void _EnterTree()
{
    Atoms.R.Watch(this, Sync);
}

private void Sync(IWatchReader R)
{
    _myAtomDropdown.Clear();
    foreach (var atom in R.Get(MyAtom.MyAtoms))
        _myAtomDropdown.AddItem(atom.Name, atom.Id);
}
```

The `Sync` delegate won't actually run every time *any* atom changes. Instead, `Watch` will schedule the `Sync` to run once at the end of the current frame, at which point `Sync` will subscribe to any changes in atoms that it reads through `IWatchReader R`, which has the same interface for reading atoms as `Atoms.R`. Whenever any atom that was accessed is updated, the `Sync` will be rerun, and its subscriptions will be regenerated from that run.

### Diff

To create and remove nodes in response to atom changes, use `R.Diff(MyAtom.MyAtoms)`. `Diff` returns lists of atoms added and removed since the last `Sync`. On the first `Sync`, however, all atoms are returned as "added".

```C#
public class MyAtomManager : Panel
{
    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    private void Sync(IWatchReader R)
    {
        var (removed, added) = R.Diff(MyAtom.MyAtoms);

        foreach (var r in removed)
            RemoveAtomChild(r.Id);
        foreach (var a in added)
            AddAtomChild(a.Id);
    }
}
```

You can also perform a `Diff` on a `Lookup` with `R.Diff(MyAtom.ByParent, parentId)`.

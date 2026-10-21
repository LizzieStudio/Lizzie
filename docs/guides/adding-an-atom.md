# Adding an Atom

Atoms are the synchronized state of a Lizzie game. When you implement an atom, you get
* an object which is synchronized to every client
* undo/redo for the user
* an object which is saved with the project
* an observable API for reactive user interfaces

## Parts of an Atom

The simplest atom inherits from `Atom` and has a `[JsonName("")]`, an `Id`, and a `ById` query. We'll explain these parts below.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    public required SnowTag Id { get; init; }

    public static readonly Identity<MyAtom, SnowTag> ById = new(r => r.Id);
}
```

The `JsonName("MyAtom")` attribute tells Lizzie what this atom should be called in the save file. You can just set it to the classname.

The `SnowTag Id` could be any type and name you want, but a `SnowTag` is standard. You get a new SnowTag by calling `SnowTag.Create()` and they are guarenteed unique to the game.

The `Identity<MyAtom, SnowTag> ById = new(r => r.Id)` is a query which every atom needs. It tells Lizzie how to get this atom and differentiate it from other atoms. You can use a different type than `SnowTag` for the `Id` and `Identifier` query, which we'll go into more below, but almost all atoms just stick to `SnowTag`s, so we'll use that for now.

Importantly, atoms should be made immutable by using `{ get; init; }` properties. Atoms are kept in an event-log so that the user can undo and redo changes, so you shouldn't update an atom once created.

## Reading and Writing

You don't have to do anything special to instantiate atoms. They're treated like normal objects most of the time. When you're ready to write the atom to the all clients, call `AtomService.Instance.Write(myAtom)`. You can then read it back out on any client with `AtomService.Instance.Get(MyAtom.ById, itsId)`.

Atoms are immutable, so you don't update them by setting their properties. You update them by writing a new one with the same Id. This is why an `Identifier` query is required. A new record of the same type and with the same id is considered to be a replacement for the previous one. Records make this easy by giving you a `with` operator.

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

#### Get All

Of course, other clients won't know about the `Id` of atoms to look them up with, so you'll probably also want an `All<>` query somewhere. This gives you access to all `MyAtom`s with `AtomService.Instance.Get(MyAtom.All)`.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    // ...
    public static readonly All<MyAtom> MyAtoms = new();
}
```

You can optionally add a filter to `All` to get all of `MyAtom` that pass the filter: `All<MyAtom> MyAtoms = new(a => a.Showing)`.

`All` is useful, but you should only use it on atoms for which you actually want to show them all to the user, like the Prototypes or DataSets, since `All` will store all the atoms to the project save.

### Groups

The last query type is the `Lookup<>` query. A `Lookup<>` sorts all of the atoms into keys which can have multiple items, which are often SnowTags. Return `null` if you don't want the atom to show up under any key.

```C#
[JsonName("MyAtom")]
public record MyAtom : Atom
{
    // ...
    public SnowTag? ParentRef { get; init; } = null;
    public static readonly Lookup<MyAtom, SnowTag> ByParent = new((a) => a.ParentRef);
}
```

You can then query for the atoms under a key with `AtomService.Instance.Get(MyAtom.ByParent, parentId)`.

### Only

Sometimes you only need one instance of an atom per game. For this you can replace the `Identity` query with a `Singleton` query.

```C#
[JsonName("ProjectGameSettings")]
public sealed record ProjectGameSettings : Atom
{
    public static readonly Singleton<ProjectGameSettings> Only = new();
    // ...
}
```

It can then be retreived without any argument using `AtomService.Instance.Get(MyAtom.Only)`. Any write to a `Singleton` atom will overwrite the one shared atom.

### The standard structure

You generally shouldn't have atoms have each other's SnowTag (a bidirectional reference) as that will create two "sources of truth" for the same relationship. Atoms on different clients can overwrite each other frequently, and the easiest way to prevent corruption is to ensure that two different atoms can never "disagree" on the truth.

The standard structure of atoms is to have "root" atoms that have an `All` query, like the DataSets, then "children" atoms that reference the roots by `Lookup` queries, like the DataRows and DataColumns. This allows any client to update any atom at any time, and the app will stay in an acceptable state.

## Undo/Redo

You don't have to do anything to make Undo and Redo work for the user. When you write a atom, the user can undo that write like they can any other. The main thing to understand is that atom updates that you `Write` together are undone together, so you should write related changes as one `Write` call: `AtomService.Instance.Write(a, b, c, ...)`.

### Undo Groups

If the user starts an action with multiple discrete events that should be undone or redone together, you can combine these events into one undo group with `Open`, `Append`, and `Close`.

```C#
var groupId = AtomService.Instance.Open(myAtom);
// ...
AtomService.Instance.Append(groupId, someOtherAtom);
// ...
AtomService.Instance.Append(groupId, someThirdAtom);
// ...
AtomService.Instance.Close(groupId, someThirdAtom);
```

While an undo group is "open", it cannot be Undone or Redone. Once it's "closed", Undo and Redo events will reverse or bring back all of the events as a unit. This is useful for "gestures", like dragging components, or for repeated edits to a focused input, like a number picker.

You can also Cancel the group with `CancelIfOpen`. When you cancel a group, all of its events are reversed, but without create an Undo event (so the users Redo doesn't reapply them). This is used for things like the user clicking the Escape button from a focused input which they've already changed.

```C#
// no writes because the group is going away
AtomService.Instance.CancelIfOpen(groupId);
```

## Saving

When the user saves the project, Lizzie spiders through the records checking to see which are accessible from the queries alone, and any that can't be reached are discarded. Lizzie starts by marking any atoms that are freely accessible without parameters as live:
* an atom returned by an `All` query
* an atom returned by a `Lookup` on a primitive key, like a string
* an atom with a primitive `Identity`, like a string
It then takes the SnowTags that those atoms serialized and starts marking atoms as live if they are accessible with those SnowTags:
* an atom returned by a `Lookup` on a SnowTag key that is already being serialized
* an atom with a SnowTag `Identity` that is already being serialized

The rule here is that if you can't access it anymore, it's not kept in the save, kind of like our own form of garbage collection. This is why it's important to use the queries and not just add `All` to everything, because it will keep everything around forever. Add the queries you need, but no more.

### NotSaved

If you know you don't want to save an atom, you can give it the `NotSaved` attribute. It will still sync to other clients and have all other features, but if you save and reload the project, they won't be there. We currently use this for keeping track of user selection, seating, and the current running game. Be warned that a SnowTag reference to a `NotSaved` atom will dangle after you save and reload. Try to avoid references from normal atoms to `NotSaved` atoms.

```C#
[NotSaved]
[JsonName("Selection")]
public record Selection : Atom
{
    // ...
}
```

## The Reactive API

You'll often need to keep Godot Nodes in sync with the atoms. For this, there is a simple reactive API. You can call `AtomService.Instance.Watch(myNode, SyncDelegate)` inside of a node's `_EnterTree` to run SyncDelegate every time the atoms change.

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

The `Sync` delegate won't actually run every time any atom changes. Instead, `Watch` will schedule the `Sync` to run once at the end of the current frame, at which point `Sync` will subscribe to any changes in atoms that it reads through `IWatchReader R`, which has the same interface for reading records as `AtomService.Instance`. Whenever any atom that was accessed is updated, the `Sync` will be rerun, and its subscriptions will be regenerated from that run. This simulates "running whenever an atom changes" without actually rerunning every `Watch` callback.

You don't need to unsubscribe on `_ExitTree`. `AtomService` will do that for you. If the Node ever comes back, Godot will run `_EnterTree` again, which will wire up the `Watch` again.

### Diff

`Sync` is helpful once you have a Node to represent an atom, but there is usually a parent Node which has to create those children Nodes. For that, there is the `Diff` method. This is only available on the `IWatchReader` passed to a `Watch` callback. It returns lists of atoms added and removed since the last `Sync` so that you can create or remove their corresponding Nodes. On the first `Sync`, all atoms are returned as "added" so that you can reuse the same `Sync` for initial setup as well.

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
            RemoveImageTile(r.Id);
        foreach (var a in added)
            AddImageTile(a.Id);
    }
}
```

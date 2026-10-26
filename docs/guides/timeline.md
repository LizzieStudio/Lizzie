# The Multiplayer Timeline

Multiplayer uses an event sourced system for eventual consistency. When one client creates or alters a component, that change fires to all clients (including the local one) as a collection of atoms. All clients then apply those changes in response to the event.

## Atoms

Atoms are the immutable, synchronized objects that drive multiplayer and project saves. See [Adding an Atom](./adding-an-atom.md).

## SnowTags

A SnowTag is a 32 bit ID that is unique to the project. SnowTags are used to identify atoms.

## SnowportIds

The SnowportId is a 64 bit timestamped ID that is also unique to the project. SnowportIds are used to identify the events that communicate atom changes. SnowportIds have a guaranteed order when two clients have communicated in-between their minting. So, if Red Client sends event A to Blue Client, events that Blue Client generates afterwards are guaranteed to use a larger SnowportId than event A. This is referred to as a [hybrid clock](https://martinfowler.com/articles/patterns-of-distributed-systems/hybrid-clock.html).

## Events, Commands, and Atoms

An event carries two parts: the Command that triggered it, if any, and an array of atoms, if there are any. The Command helps to convey what prompted the event, while the atom updates specify what has changed. Some events won't have the command or won't have any atoms.

Every event is kept in the Timeline in SnowportId order. The Timeline is the source of truth: the current state of atoms is derived from it, and undo and redo are worked out from it.

## Clients Joining

Clients who join receive the full Timeline, from oldest to newest. This ensures undo/redo still works.

## Editing and saving a project

Projects are saved as a single event with all live atoms in a JSON-lines file (a file where each line is a valid JSON object). The atoms are pruned by first keeping any atoms that are accessible through their queries, then spidering their SnowTag references to each other. In this way, only accessible atoms are kept.

## Events and Undo

Clients can Undo an Event by publishing an Undo Event that names a prior Event by SnowportId. All clients will add the Undo Event to their Timeline and then reverse the undone event. To reverse the event, the clients perform a linear scan backwards through the Timeline looking for the previous state of atoms that the undone event changed.
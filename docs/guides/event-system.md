# The Multiplayer Event system

Multiplayer uses an event sourced system for eventual consistency. When one client creates or alters a component, that change fires to all client (including the local one) as a collection of "upserts". All clients then apply those changes in response to the event. This document describes how this works.

## Atoms

Atoms are the immutable, synchronized objects that drive multiplayer and project saves. Atoms are updated and distributed in events, and each event is undoable by the user using Undo and Redo commands. They also provide a reactive API to update Godot Nodes in real time. They are used to represent components, prototypes, templates, datasets, assets, and anything else that needs to be synchronized or saved.

## SnowTags

A SnowTag is a 32 bit ID that is unique to the project. SnowTags are used to identify atoms.

## SnowportIds

The SnowportId is a 64 bit timestamped ID that is also unique to the project. SnowportIds are used to identify the events that communicate atom changes. SnowportIds have a guarenteed order when two clients have communicated in-between their minting. So, if Red Client sends event A to Blue client, events that Blue client generate afterwards are guarenteed to use a larger SnowportId than event A.

## Events, Commands, and Effects

Events carry two parts: the Command that triggered it, if any, and an array of atom updates, if there are any. The Command helps to convey what prompted the event, while the atom updates specify what has changed. Some events won't have either the command or any updates.

Every event is kept in the Timeline, in SnowportId order. The Timeline is the source of truth: the atoms are worked out from it, and undo and redo read it.

## Atom "upserts"

Atom updates are "upserts" that identify atoms by their SnowTag. An upsert carries the full internal state of the atom. If an upsert arrives and an atom of the given type and SnowTag doesn't exist yet, it is created. If it arrives and the type and SnowTag does exist, it is updated.

## Order of events

The last update to an atom is tracked internally using the SnowportId of the write event. When a new event arrives, its effects are only applied to those atoms who haven't been updated more recently. This ensures that events can arrive out of order on different clients without affecting eventual consistency.

## Clients Joining

Clients who join receive the full Timeline, from oldest to newest. This ensures undo/redo still works.

## Editing and saving a project

Projects are saved as a list of record update events in a JSON-lines file (a file where each line is a valid JSON object). The atoms are pruned by spidering their SnowTag references to each other so that only accessible atoms are kept.

## The cursor position

The only synchronized state that doesn't use the events system is the cursor position for each user. These are streamed using Godot's unreliable connection. The cursor position is then used for animating dragged components. Dragging starts by firing a drag event, but the interim positions of the dragged components is then interpolated on each client from the cursor position. When the dragging client drops the components, they fire a drop event containing the exact final location of components, which all other clients use to correct the state of the dragged components.

## Events and Undo

Clients can Undo an Event by publishing an Undo Event that names a prior Event by SnowportId. All other clients will add the Undo Event to their event log and then reverse the effects of the undone event. To reverse these effects, the clients perform a linear scan backwards through the event log looking for previous effects that set the state of each record affected by the undone event.
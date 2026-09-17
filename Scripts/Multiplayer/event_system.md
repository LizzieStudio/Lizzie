# The Multiplayer Event system

Multiplayer uses an event sourced system for eventual consistency. When one client creates or alters a component, that change fires to all client (including the local one) as a collection of "upserts". All clients then apply those changes in response to the event. This document describes how this works.

## SnowportIds

First is the SnowportID system in SnowportId.cs. The IDs combine a 45 bit hybrid clock with an 8 bit source identifier in a long, guaranteeing their uniqueness as long as all clients use a unique source ID, which the host provides (the host gets 0). SnowportIDs are used to identify events.

SnowportIds have a guarenteed order when two clients have communicated. So, if Red Client sends event A to Blue client, and events that Blue client generates afterwards is guarenteed to use a larget SnowportId than event A. SnowportIds are "monotonically increasing".

## SnowTags

SnowTags are a second type of ID also found in SnowportId.cs. SnowTags are 32 bits long, with 8 bits for the same source identifier as SnowportIds, and 24 bits for a sequential incrementing counter. SnowTags also give a unique ID, but are used to identify records: components, prototypes, templates, datasets, assets, etc. These ids are used when events identify records for changes. They can also be used as the Id in Godot UI elements, like an OptionButton.

## Events and Actions

Events will have two parts: a single Action and an Effect array. The action will specify what prompted this event, and will be used to determine which animations should apply to the event, if any. The Effect array will specify what has actually changed about the table, separated by the record that they touch. Some events may only have an action or effects. A delete event, for example, may only have the effects, since the action is self explanatory. A ping event, on the other hand, may only have the action, since no records need to change.

## The Effect "upserts"

Effects are mostly modeled as "upserts" on records identified by SnowTag. These carry the full internal state of components, prototypes, datasets, etc. If an upsert arrives and the SnowTag doesn't exist yet, that record is created. If it arrives and the SnowTag does exist, it is updated. "Deletion" uses a flag on the record to "soft delete" the record, making it easy to reverse for undo/redo. This also means that upserts are used to delete components.

## Order of events

Records have a lastWriteId that holds the SnowportId of the last event to update them. When a new event arrives, its effects are only applied to those components with a lastWriteId that is older than the incoming event's id. Otherwise the effect is ignored for that record. This ensures that events can arrive out of order on different clients without affecting eventual consistency.

## Clients Joining

For now, clients who join the game will receive the full event log from oldest to newest. This ensures undo/redo still works. In the future, we can revisit this and make it where events are only streamed whenever an undo happens.

## Editing and saving a project

Projects are saved as a list of record update events in a JSON-lines file (a file where each line is a valid JSON object). At the moment all upserts are stored. In the future, we'll want a compressed version of these files with only the most recent upserts and Snowtags renormalized to start at 0.

## The ZOrder

The new ZOrder, the total ordering of components used to determine stack order, is likely the most esoteric part of the system. It is designed to keep events from having to touch uninvolved components when moving components to the top or bottom of the stack. When a component is in a container, like a deck, its ZOrder specifies its position in the container. When a component is on the table, its ZOrder sets its stacking order among the components on the table.

The ZOrder uses three properties. First, there is the ZTarget enum of Top or Bottom, which specifies whether or not the component was most recently moved to the top or bottom of the ZOrder. Then there is the LastZEvent, which holds the SnowportId of the last event which changed the components ZOrder (sending it to the top or bottom). Finally, there is the ZSuborder, which is used by events to set the ZOrder of the list of components that it changed. The ZSuborder must have a total ordering of the components in that event. The ZTarget enum will also have an Unset option, which allows Transform events to avoid changing a components ZOrder.

The effective ZOrder is then
1. Top components are higher than Bottom components.
2. For Top components, newer (greater) LastZEvent is higher than older LastZEvent.
3. For Bottom components, older (lower) LastZEvent is higher than newer LastZEvent.
4. If all of these were equal, higher ZSuborder components are higher than lower ZSuborder components.

## Tracking Children

The set of cards in a deck are tracked on the cards by a containerId property with the SnowTag of the deck. The containerId is the source-of-truth on containment. The deck maintains a cache of its children, but it is only a cache. The order of components, as stated above, is determined by their ZOrder. Any changes in containment must change the containerId of the children and any changes in order inside a container, like shuffling, must change the children's ZOrder.

This is more resilient to bugs. If two clients pulled a card off of a deck at the same time, the host would need to consolidate what that means for the deck's "children" array. With the containerId property, one client would just "wipe-out" the other clients changes as one of the writes to containerId wins.

## Shuffle and Roll

Shuffle and roll actions transmit the final position of all components that they moved. The original client performs the RNG and shuffle, finds an update for the positions of the components, then creates upsert effects to move them to those positions.

## The cursor position

The only synchronized state that doesn't use the events system is the cursor position for each user. These are streamed over RPC calls. The cursor position is then used for animating dragged components. Dragging starts by firing a drag event, but the interim positions of the dragged components is then interpolated on each client from the cursor position. When the dragging client drops the components, they fire a drop event containing the exact final location of components, which all other clients use to correct the state of the dragged components.

## Client authority

The host does not serve as the total source of truth, except with some events having to do with joining and leaving. Instead, each client is trusted to produce unique SnowTags and unique SnowportIds with monotonically increasing hybrid clocks and correct time offsets. A user with a hacked client will be able to cheat for now. Conflicts will be handled uniformly across clients, same as with Conflict Free Replicated Data Types. For example, if two users select the same component, whoever did so second (most recently) wins. The host doesn't pick, the time in the SnowportId does.

## Events and Undo

Clients can Undo an Event they created by publishing an Undo Event that names a prior Event by SnowportId. All other clients will add the Undo Event to their event log and then reverse the effects of the undone event. To reverse these effects, the client will perform a linear scan backwards through the event log looking for previous effects that set the state of each record affected by the undone event, effectively recreating the state of records from scratch.

Undo works in a specific way to avoid loss of state in the history. When you start to undo events, the history travels backwards, [A, B, C] -> [A, B] -> [A]. If you then add a new event, however, the undo history "unwraps" into the edit history, similar to emacs, [A] -> [A, B, C, -C, -B, A, D]. If you undo from there, the history walks back again, [A, B, C, -C, -B, A, D, -D], and so on. This ensures that history is never lost.

Internally, when a user triggers Undo, the system does a linear scan backwards through the event log looking only at events created by the local source. While its doing this, it keeps a stack of Undo events and their target event (the undone event). For as long as it keeps encountering Undo events, it will keep adding target events to the stack. Once it encounters any non-undo events, it will switch to removing them from the stack (assuming they match). Then, once it encounters an event not matching the top of the stack, (which includes encountering an event after the stack has cleared), it will issue an Undo targetting that event. Sometimes this old event is an Undo event, which gives us the unwrap feature. Redo events are just Undo events targetting Undo events. As long as the user only creates Undo and Redo events, the timeline will move backwards and forwards as expected.

# Future changes

These changes will be made later; not right now.

## Old-Event Rejection

The host should reject timestamps that are too far in the past or future. For example, if an event arrives that is 3 seconds in-front or behind the host's hybridClock, the host will refuse to transmit it, returning an error to the offending client. That client will then need to correct its state to match the host's authoritative list of events and continue from there.

## Host Transfer

When the host disconnects or drops, the client with the lowest SnowportId will claim themselves as host. This will come with an effect. They will then request events from all their peers based on the last SnowportId that they received from each peer. The peer will then send back any events that occured since that SnowportId and will continue to send events to this new host from then on.
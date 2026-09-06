# Multiplayer System

This document describes the multiplayer networking system for collaborative game design in Lizzie Studio.

## Overview

The multiplayer system allows multiple users to work on the same project simultaneously. It synchronizes:
- **Project data** (datasets, prototypes, templates)
- **Game objects** (position, rotation, z-order)
- **Drag ownership** (each drag is replicated as a Drag/Drop event; the most recent grab of a component wins, so players can take a component from one another)

## Architecture

The system consists of four main components:

### 1. MultiplayerManager (`Scripts/Multiplayer/MultiplayerManager.cs`)
Central manager for network connections and player state.

**Key Features:**
- Host or join servers using ENet protocol
- Track connected players
- Handle connection/disconnection events
- Provide server authority model

**Events:**
- `ServerStarted` - Emitted when server starts successfully
- `PlayerConnected(int playerId)` - Emitted when a player joins
- `PlayerDisconnected(int playerId)` - Emitted when a player leaves
- `ConnectionFailed` - Emitted when client fails to connect

### 2. ProjectSynchronizer (`Scripts/Multiplayer/ProjectSynchronizer.cs`)
Synchronizes project-level data across the network.

**What it syncs:**
- Entire project structure (but not prototypes*)
- Individual datasets
- Individual templates

*Prototypes are now synced through `EventSynchronizer`.

**How it works:**
- Listens to EventBus for local changes (DataSetChangedEvent, TemplateChangedEvent)
- Sends changes to server via RPC
- Server broadcasts to all clients
- Clients apply changes without re-triggering sync events

### 3. MultiplayerDialog (`Scripts/Multiplayer/MultiplayerDialog.cs`)
UI window for hosting/joining multiplayer sessions.

**Features:**
- Host server tab (configure port and max players)
- Join server tab (enter address and port)
- Connected players list
- Connection status display
- Disconnect button

### 4. MultiplayerDialogManager (`Scripts/Multiplayer/MultiplayerDialogManager.cs`)
Helper to spawn and manage the multiplayer dialog.

**Usage:**
- Press F8 to toggle the dialog (configurable in `_Input` method)
- Can be called from menu items or buttons

## Setup Instructions

### 1. Add AutoLoad Singletons

In your Godot project settings, add these scripts as AutoLoad singletons (in this order):

1. **EventBus** → `Scripts/EventBus.cs`
2. **ProjectService** → `Scripts/Project/ProjectService.cs`
3. **MultiplayerManager** → `Scripts/Multiplayer/MultiplayerManager.cs`
4. **ProjectSynchronizer** → `Scripts/Multiplayer/ProjectSynchronizer.cs`

### 2. Add MultiplayerDialogManager to Your Scene

Add `MultiplayerDialogManager.cs` as a child node of your main UI or game controller.

### 3. Ensure GameObjects is in Your Scene

The `GameObjects` node should be part of your main game scene (already integrated if it exists).

## Usage

### Hosting a Server

1. Press F8 to open the multiplayer dialog
2. Go to the "Host Server" tab
3. Configure:
   - **Port**: Default is 7777 (change if needed)
   - **Max Players**: Maximum 32 concurrent players
4. Click "Host Server"
5. Status will show "Hosting on port XXXX"
6. Share your IP address with other players

### Joining a Server

1. Press F8 to open the multiplayer dialog
2. Go to the "Join Server" tab
3. Configure:
   - **Server Address**: Host's IP address (use 127.0.0.1 for local testing)
   - **Port**: Same port the host configured
4. Click "Join Server"
5. Status will show "Connected to ADDRESS:PORT"
6. Your project will sync with the host's project

### Working in Multiplayer

**Drag Ownership:**
- When you click and drag one or more objects, a Drag event is broadcast carrying each object's offset from your cursor. Every client then positions those objects at your cursor plus the offsets.
- Grabbing an object another player is already dragging simply takes it from them.
- Releasing the mouse broadcasts a Drop event with the final world positions, ending the drag on every client.
- Ownership is resolved entirely from the replicated events by their SnowportId order. The most recent drag of a component always wins.

**Project Sync:**
- Any changes to datasets, prototypes, or templates are automatically synchronized
- When the project data changes, all objects in the scene refresh automatically
- New clients joining receive the full project state immediately

**Object Sync:**
- When you spawn new objects, they appear for all players
- When you delete objects, they disappear for all players
- Position, rotation, and z-order are continuously synchronized during drags

## Code Integration Examples

### Accessing Multiplayer State

```csharp
// Check if multiplayer is active
if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
{
    // Multiplayer logic here
}

// Check if this is the server
if (MultiplayerManager.Instance?.IsServer == true)
{
    // Server-only logic
}

// Get local player ID
int myId = MultiplayerManager.Instance.LocalPlayerId;

// Iterate over all players
foreach (var player in MultiplayerManager.Instance.Players.Values)
{
    GD.Print($"Player: {player.PlayerName}, ID: {player.PlayerId}");
}
```

### Custom RPC Methods

If you need custom networking for your own features:

```csharp
public partial class MyCustomNode : Node
{
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void MyRpcMethod(string data)
    {
        // Handle RPC call
        GD.Print($"Received: {data}");
    }
    
    public void SendData()
    {
        if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
        {
            Rpc(nameof(MyRpcMethod), "Hello from " + MultiplayerManager.Instance.LocalPlayerId);
        }
    }
}
```

## Network Architecture

**Server Authority Model:**
- The server is authoritative for all game state
- Clients send requests to the server
- Server validates and broadcasts approved changes

**Drag Flow:**
1. Client broadcasts a Drag event via the EventSynchronizer, listing each component and its offset from the dragging player's cursor.
2. Every client repositions those components each frame at the source player's synchronized cursor plus their offsets. Because the offsets travel in the event, all clients agree on the positions during the drag.
3. Grabbing a component takes it from the previous source. This is set up as a natural byproduct of the newer snowport id of the second drag.
4. On release, the client broadcasts a Drop event with the final world positions, which every client applies.

**Transform Sync Flow:**
1. Client drags object (after acquiring lock)
2. Every 3 frames, position/rotation sent to server
3. Server broadcasts to all other clients (not the sender)
4. Clients apply transform updates

**Project Sync Flow:**
1. User modifies dataset/prototype/template
2. EventBus publishes change event
3. ProjectSynchronizer sends to server
4. Server broadcasts to all clients
5. Clients apply changes using `SetProjectSilent()` to avoid recursive sync

## Performance Considerations

**Bandwidth:**
- Transform updates are throttled (every 3 frames)
- Only changes are sent, not full state
- Uses unreliable_ordered for transform updates (lost packets are acceptable)
- Uses reliable for important events (lock/unlock, creation/deletion)

**Latency:**
- Transform updates may lag by 50-150ms depending on network
- Lock requests have round-trip latency (client → server → all clients)
- Project sync is immediate but may take time for large projects

**Scaling:**
- Tested for up to 8 concurrent players
- Server can be configured for up to 32 players
- Performance depends on number of objects and update frequency

## Troubleshooting

**Connection Issues:**
- Ensure port is not blocked by firewall
- Use port forwarding if connecting over the internet
- Verify IP address is correct (use `ipconfig` on Windows or `ifconfig` on Linux/Mac)

**Sync Issues:**
- Check console for error messages
- Ensure all clients are running the same version
- Try disconnecting and reconnecting
- Restart server if project sync fails

**Drag Issues:**
- Only one player owns a given component's drag at a time; grabbing a component another player is dragging takes it from them.
- If a dragging player disconnects mid-drag, another player can select the components to correct their state.

## Future Enhancements

Potential improvements for future versions:
- Voice chat integration
- Player cursors/presence indicators
- Undo/redo synchronization
- Conflict resolution for simultaneous edits
- Persistent sessions (save/load multiplayer state)
- Lobby system for easier matchmaking
- Roles and permissions (host vs. guest capabilities)

## API Reference

### MultiplayerManager

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `HostServer` | `int port, int maxPlayers` | `Error` | Start hosting a server |
| `JoinServer` | `string address, int port` | `Error` | Connect to a server |
| `Disconnect` | - | `void` | Disconnect from current session |

| Property | Type | Description |
|----------|------|-------------|
| `IsMultiplayerActive` | `bool` | True if connected to a session |
| `IsServer` | `bool` | True if this instance is the server |
| `LocalPlayerId` | `int` | This player's unique ID |
| `Players` | `IReadOnlyDictionary<int, PlayerInfo>` | All connected players |

### ProjectSynchronizer

| Method | Description |
|--------|-------------|
| `RequestProjectSync()` | Request full project from server (used when joining) |

---

**Note:** This system uses Godot's built-in ENet multiplayer peer, which is suitable for LAN and internet play with port forwarding. For cloud-based matchmaking, consider integrating Nakama, PlayFab, or similar services.

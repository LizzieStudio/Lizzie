using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Tracks which connection owns which player position.
/// The server is the authority: ClaimSeat sends an RPC to the server which
/// validates exclusivity and then broadcasts the result to all peers.
///
/// SeatIndex values:
///   0..N-1  — named player slots from GameSettings.Players
///   -1      — observer (unlimited, only when GameSettings.AllowObservers is true)
///   -2      — unclaimed (default before a seat is chosen)
/// </summary>
public partial class PlayerSeatManager : Node
{
    private static PlayerSeatManager _instance;
    public static PlayerSeatManager Instance => _instance;

    // peerId -> seatIndex
    private readonly Dictionary<int, int> _claims = new();

    public IReadOnlyDictionary<int, int> Claims => _claims;

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
        {
            QueueFree();
            return;
        }
        _instance = this;
    }

    public override void _ExitTree()
    {
        if (_instance == this)
            _instance = null;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the given seat index is not yet claimed by any connection.
    /// Always returns true for observer seats (-1).
    /// </summary>
    public bool IsAvailable(int seatIndex)
    {
        if (seatIndex == -1) // observers are unlimited
            return true;
        return !_claims.Values.Contains(seatIndex);
    }

    /// <summary>
    /// Returns the seat index for a given peer, or -2 if unknown.
    /// </summary>
    public int GetSeat(int peerId) => _claims.TryGetValue(peerId, out var seat) ? seat : -2;

    /// <summary>
    /// Request to claim a seat for the local player.
    /// In multiplayer the request is forwarded to the server; locally it is applied immediately.
    /// </summary>
    public void ClaimSeat(int seatIndex)
    {
        var mm = MultiplayerManager.Instance;
        if (mm == null || !mm.IsMultiplayerActive)
        {
            // Local-only mode: just record and broadcast the event locally.
            ApplyClaim(mm?.LocalPlayerId ?? 1, seatIndex, accepted: true);
            return;
        }

        // Ask the server (peer 1) to validate and broadcast.
        RpcId(1, nameof(ServerReceiveClaimRequest), mm.LocalPlayerId, seatIndex);
    }

    /// <summary>
    /// Release the seat held by a given peer (called on disconnect or voluntary leave).
    /// </summary>
    public void ReleaseSeat(int peerId)
    {
        if (!_claims.ContainsKey(peerId))
            return;

        _claims.Remove(peerId);

        // Update PlayerInfo if available
        if (MultiplayerManager.Instance?.Players.TryGetValue(peerId, out var pi) == true)
            pi.PlayerPosition = -2;

        EventBus.Instance?.Publish(
            new PlayerSeatClaimedEvent
            {
                PeerId = peerId,
                SeatIndex = -2,
                Accepted = true,
            }
        );
    }

    // -------------------------------------------------------------------------
    // Server-side RPC: receives claim request, validates, broadcasts result
    // -------------------------------------------------------------------------

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ServerReceiveClaimRequest(int requestingPeerId, int requestedSeat)
    {
        if (!MultiplayerManager.Instance.IsServer)
            return;

        var settings = ProjectService.Instance.CurrentProject?.GameSettings;
        if (settings == null)
        {
            RpcId(
                requestingPeerId,
                nameof(ClientReceiveClaimResult),
                requestingPeerId,
                requestedSeat,
                false
            );
            return;
        }

        bool accepted;
        if (requestedSeat == -1)
        {
            // Observer – allowed only if the project permits it
            accepted = settings.AllowObservers;
        }
        else if (requestedSeat < 0 || requestedSeat >= settings.Players.Count)
        {
            accepted = false;
        }
        else
        {
            // Accept if not already taken by someone else
            accepted = !_claims.TryGetValue(requestingPeerId, out _)
                ? IsAvailable(requestedSeat)
                : IsAvailableExcluding(requestedSeat, requestingPeerId);
        }

        if (accepted)
        {
            // Release any previous seat this peer held
            _claims.Remove(requestingPeerId);
            _claims[requestingPeerId] = requestedSeat;

            // Broadcast the accepted result to every peer (including the requester)
            Rpc(nameof(ClientReceiveClaimResult), requestingPeerId, requestedSeat, true);
        }
        else
        {
            // Inform only the requester that the seat was rejected
            RpcId(
                requestingPeerId,
                nameof(ClientReceiveClaimResult),
                requestingPeerId,
                requestedSeat,
                false
            );
        }
    }

    // -------------------------------------------------------------------------
    // Client-side RPC: receives result from server
    // -------------------------------------------------------------------------

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void ClientReceiveClaimResult(int peerId, int seatIndex, bool accepted)
    {
        ApplyClaim(peerId, seatIndex, accepted);

        var mm = MultiplayerManager.Instance;
        // If rejected AND it was our request, re-open the dialog
        if (!accepted && mm != null && peerId == mm.LocalPlayerId)
        {
            GD.Print($"[PlayerSeatManager] Seat {seatIndex} rejected – prompting again");
            EventBus.Instance?.Publish(new RequestPlayerPositionEvent());
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ApplyClaim(int peerId, int seatIndex, bool accepted)
    {
        if (accepted)
        {
            _claims[peerId] = seatIndex;

            if (MultiplayerManager.Instance?.Players.TryGetValue(peerId, out var pi) == true)
                pi.PlayerPosition = seatIndex;
        }

        EventBus.Instance?.Publish(
            new PlayerSeatClaimedEvent
            {
                PeerId = peerId,
                SeatIndex = seatIndex,
                Accepted = accepted,
            }
        );
    }

    private bool IsAvailableExcluding(int seatIndex, int excludePeerId)
    {
        if (seatIndex == -1)
            return true;
        return !_claims
            .Where(kv => kv.Key != excludePeerId)
            .Select(kv => kv.Value)
            .Contains(seatIndex);
    }

    /// <summary>
    /// Called by the server after a full project sync to push the current seat map
    /// to a newly-joined client so they see who is already seated.
    /// </summary>
    public void PushSeatMapToClient(int clientPeerId)
    {
        if (!MultiplayerManager.Instance.IsServer)
            return;

        foreach (var kv in _claims)
        {
            RpcId(clientPeerId, nameof(ClientReceiveClaimResult), kv.Key, kv.Value, true);
        }
    }
}

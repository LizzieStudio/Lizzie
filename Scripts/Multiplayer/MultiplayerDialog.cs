using System;
using Godot;

/// <summary>
/// UI dialog for hosting or joining multiplayer sessions
/// </summary>
public partial class MultiplayerDialog : Window
{
    private LineEdit _portInput;
    private SpinBox _maxPlayersInput;
    private Button _hostButton;
    private LineEdit _addressInput;
    private LineEdit _clientPortInput;
    private Button _joinButton;
    private Button _disconnectButton;
    private Label _statusLabel;
    private ItemList _playerList;

    private const int DefaultPort = 7777;
    private const int DefaultMaxPlayers = 4;

    public override void _Ready()
    {
        Title = "Multiplayer";
        Size = new Vector2I(500, 400);
        Exclusive = false;

        BuildUI();

        // Subscribe to multiplayer events
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.ServerStarted += OnServerStarted;
            MultiplayerManager.Instance.PlayerConnected += OnPlayerConnected;
            MultiplayerManager.Instance.PlayerDisconnected += OnPlayerDisconnected;
            MultiplayerManager.Instance.ConnectionFailed += OnConnectionFailed;
        }

        UpdateUI();

        OnDisconnectPressed(); // Ensure we start in a disconnected state
    }

    public override void _ExitTree()
    {
        // Unsubscribe from events
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.ServerStarted -= OnServerStarted;
            MultiplayerManager.Instance.PlayerConnected -= OnPlayerConnected;
            MultiplayerManager.Instance.PlayerDisconnected -= OnPlayerDisconnected;
            MultiplayerManager.Instance.ConnectionFailed -= OnConnectionFailed;
        }
    }

    private void BuildUI()
    {
        _statusLabel = GetNode<Label>("%StatusLabel");
        _portInput = GetNode<LineEdit>("%PortInput");
        _maxPlayersInput = GetNode<SpinBox>("%MaxPlayerInput");
        _hostButton = GetNode<Button>("%HostButton");
        _addressInput = GetNode<LineEdit>("%AddressInput");
        _clientPortInput = GetNode<LineEdit>("%ClientPortInput");
        _joinButton = GetNode<Button>("%JoinButton");
        _playerList = GetNode<ItemList>("%PlayerList");
        _disconnectButton = GetNode<Button>("%DisconnectButton");

        //_portInput.Text = DefaultPort.ToString();

        _maxPlayersInput.Value = DefaultMaxPlayers;

        _hostButton.Pressed += OnHostPressed;

        _addressInput.Text = "127.0.0.1";

        _portInput.Text = DefaultPort.ToString();
        _clientPortInput.Text = DefaultPort.ToString();

        _joinButton.Pressed += OnJoinPressed;

        // Disconnect button at bottom
        _disconnectButton.Pressed += OnDisconnectPressed;
    }

    private void UpdateUI()
    {
        bool isConnected = MultiplayerManager.Instance?.IsMultiplayerActive == true;
        bool isServer = MultiplayerManager.Instance?.IsServer == true;

        _hostButton.Disabled = isConnected;
        _joinButton.Disabled = isConnected;
        _portInput.Editable = !isConnected;
        _addressInput.Editable = !isConnected;
        _clientPortInput.Editable = !isConnected;
        _maxPlayersInput.Editable = !isConnected;
        _disconnectButton.Disabled = !isConnected;

        if (!isConnected)
        {
            _statusLabel.Text = "Not connected";
            _playerList.Clear();
        }
        else if (isServer)
        {
            _statusLabel.Text = $"Hosting on port {_portInput.Text}";
        }
        else
        {
            _statusLabel.Text = $"Connected to {_addressInput.Text}:{_clientPortInput.Text}";
        }

        UpdatePlayerList();
    }

    private void UpdatePlayerList()
    {
        _playerList.Clear();

        if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
        {
            foreach (var player in MultiplayerManager.Instance.Players.Values)
            {
                var displayName = player.IsLocal ? $"{player.PlayerName} (You)" : player.PlayerName;
                _playerList.AddItem(displayName);
            }
        }
    }

    private void OnHostPressed()
    {
        if (!int.TryParse(_portInput.Text, out int port))
        {
            port = DefaultPort;
        }

        int maxPlayers = (int)_maxPlayersInput.Value;

        var error = MultiplayerManager.Instance.HostServer(port, maxPlayers);
        if (error != Error.Ok)
        {
            _statusLabel.Text = $"Failed to host server: {error}";
        }
        else
        {
            // Request project sync setup
            ProjectSynchronizer.Instance?.RequestProjectSync();
        }
    }

    private void OnJoinPressed()
    {
        string address = _addressInput.Text;
        if (string.IsNullOrWhiteSpace(address))
        {
            address = "127.0.0.1";
        }

        if (!int.TryParse(_clientPortInput.Text, out int port))
        {
            port = DefaultPort;
        }

        var error = MultiplayerManager.Instance.JoinServer(address, port);
        if (error != Error.Ok)
        {
            _statusLabel.Text = $"Failed to join server: {error}";
        }
        else
        {
            _statusLabel.Text = "Connecting...";

            // Request project sync after connection
            CallDeferred(nameof(RequestProjectSyncDeferred));
        }
    }

    private void RequestProjectSyncDeferred()
    {
        // Wait a frame for connection to establish
        GetTree().CreateTimer(0.5).Timeout += () =>
        {
            ProjectSynchronizer.Instance?.RequestProjectSync();
        };
    }

    private void OnDisconnectPressed()
    {
        MultiplayerManager.Instance?.Disconnect();
        UpdateUI();
    }

    private void OnServerStarted()
    {
        CallDeferred(nameof(UpdateUI));
    }

    private void OnPlayerConnected(int playerId)
    {
        GD.Print($"Player connected: {playerId}");
        CallDeferred(nameof(UpdateUI));
    }

    private void OnPlayerDisconnected(int playerId)
    {
        GD.Print($"Player disconnected: {playerId}");
        CallDeferred(nameof(UpdateUI));
    }

    private void OnConnectionFailed()
    {
        _statusLabel.Text = "Connection failed";
        CallDeferred(nameof(UpdateUI));
    }
}

using System;
using System.ComponentModel.Design;
using Godot;

public partial class ProjectSettings : Window
{
    // Setup tab
    private CheckButton _2dToggle;
    private CheckButton _playerHandsToggle;
    private LineEdit _tableWidth;
    private LineEdit _tableHeight;
    private OptionButton _tableUnits;
    private ColorPickerButton _tableColor;
    private OptionButton _rotationStep;

    // Players tab
    private CheckButton _observerToggle;
    private LineEdit _maxPlayers;
    private VBoxContainer _playerDefinitionContainer;

    // Game Info tab
    private LineEdit _gameTitle;
    private LineEdit _designers;
    private LineEdit _graphicDesign;
    private LineEdit _artists;
    private LineEdit _contactInfo;
    private TextEdit _visionStatement;

    // Buttons
    private Button _saveButton;
    private Button _cancelButton;

    private string _playerDefinitionScene = "res://Scenes/Project/PlayerDefinition.tscn";

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        // Setup tab
        _2dToggle = GetNode<CheckButton>("%StartIn2dToggle");
        _playerHandsToggle = GetNode<CheckButton>("%PlayerHandsToggle");
        _tableWidth = GetNode<LineEdit>("%TableWidth");
        _tableHeight = GetNode<LineEdit>("%TableHeight");
        _tableUnits = GetNode<OptionButton>("%TableUnits");
        _tableColor = GetNode<ColorPickerButton>("%TableColor");
        _rotationStep = GetNode<OptionButton>("%RotationStep");

        // Players tab
        _observerToggle = GetNode<CheckButton>("%ObserverToggle");
        _maxPlayers = GetNode<LineEdit>("%MaxPlayers");
        _maxPlayers.TextChanged += OnMaxPlayersUpdated;
        _playerDefinitionContainer = GetNode<VBoxContainer>("%PlayerDefinitionContainer");

        // Game Info tab
        _gameTitle = GetNode<LineEdit>("%GameTitle");
        _designers = GetNode<LineEdit>("%Designers");
        _graphicDesign = GetNode<LineEdit>("%GraphicDesign");
        _artists = GetNode<LineEdit>("%Artists");
        _contactInfo = GetNode<LineEdit>("%ContactInfo");
        _visionStatement = GetNode<TextEdit>("%VisionStatement");

        // Buttons
        _saveButton = GetNode<Button>("%SaveButton");
        _cancelButton = GetNode<Button>("%CancelButton");

        _saveButton.Pressed += OnSavePressed;
        _cancelButton.Pressed += OnClosePressed;
        CloseRequested += OnClosePressed;

        LoadFromProject();
    }

    public const int MAX_PLAYERS = 16;

    public Color[] _playerColors =
    {
        Colors.Blue,
        Colors.Red,
        Colors.Green,
        Colors.Yellow,
        Colors.Aqua,
        Colors.HotPink,
        Colors.LimeGreen,
        Colors.Orange,
        Colors.Brown,
        Colors.Purple,
        Colors.Beige,
        Colors.LightBlue,
        Colors.White,
        Colors.Black,
        Colors.Gray,
        Colors.LightGray,
    };

    private void OnMaxPlayersUpdated(string newText)
    {
        if (int.TryParse(newText, out var maxPlayers))
        {
            if (maxPlayers > MAX_PLAYERS)
            {
                maxPlayers = MAX_PLAYERS;
                _maxPlayers.Text = maxPlayers.ToString();
            }

            if (maxPlayers > 0)
            {
                UpdatePlayerDefinitionList(maxPlayers);
            }
        }
    }

    private void UpdatePlayerDefinitionList(int playerCount)
    {
        if (playerCount < 1)
            return;

        var curCount = _playerDefinitionContainer.GetChildren().Count;

        if (curCount == playerCount)
            return;

        if (curCount < playerCount)
        {
            for (int i = curCount; i < playerCount; i++)
            {
                var c = _playerColors[i];

                var newPd = GD.Load<PackedScene>(_playerDefinitionScene)
                    .Instantiate<PlayerDefinition>();
                newPd.SetPlayerInfo(
                    i + 1,
                    new ProjectPlayerSettings
                    {
                        Name = $"Player {i + 1}",
                        ColorA = _playerColors[i].A,
                        ColorR = _playerColors[i].R,
                        ColorG = _playerColors[i].G,
                        ColorB = _playerColors[i].B,
                    }
                );
                _playerDefinitionContainer.AddChild(newPd);
            }
        }
        else
        {
            for (int i = playerCount; i < curCount; i++)
            {
                var child = _playerDefinitionContainer.GetChild(i);
                if (child != null)
                    child.QueueFree();
            }
        }
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    public event EventHandler Closed;

    private void OnClosePressed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromProject()
    {
        var s = ProjectService.Instance.CurrentProject?.GameSettings;
        if (s == null)
            return;

        _2dToggle.ButtonPressed = s.StartIn2D;
        _playerHandsToggle.ButtonPressed = s.EnablePlayerHands;
        _tableWidth.Text = s.TableWidth.ToString();
        _tableHeight.Text = s.TableHeight.ToString();
        _tableUnits.Selected = s.TableUnits;
        _tableColor.Color = new Color(s.TableColorR, s.TableColorG, s.TableColorB, s.TableColorA);
        _rotationStep.Selected = s.RotationStepIndex;

        _observerToggle.ButtonPressed = s.AllowObservers;
        _maxPlayers.Text = s.MaxPlayers.ToString();

        // Game Info tab
        _gameTitle.Text = s.GameTitle;
        _designers.Text = s.Designers;
        _graphicDesign.Text = s.GraphicDesign;
        _artists.Text = s.Artists;
        _contactInfo.Text = s.ContactInfo;
        _visionStatement.Text = s.VisionStatement;

        SetPlayers(s);
    }

    private void SetPlayers(ProjectGameSettings gameSettings)
    {
        UpdatePlayerDefinitionList(gameSettings.MaxPlayers);
        foreach (var c in _playerDefinitionContainer.GetChildren())
        {
            if (c is PlayerDefinition pd)
            {
                var index = _playerDefinitionContainer.GetChildren().IndexOf(c);
                var settings =
                    gameSettings.Players.Count > index
                        ? gameSettings.Players[index]
                        : new ProjectPlayerSettings();
                pd.SetPlayerInfo(index + 1, settings);
            }
        }
    }

    private void OnSavePressed()
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
        {
            OnClosePressed();
            return;
        }

        var s = project.GameSettings;

        s.StartIn2D = _2dToggle.ButtonPressed;
        s.EnablePlayerHands = _playerHandsToggle.ButtonPressed;
        s.TableWidth = ParseFloat(_tableWidth.Text, s.TableWidth);
        s.TableHeight = ParseFloat(_tableHeight.Text, s.TableHeight);
        s.TableUnits = _tableUnits.Selected;
        s.TableColorR = _tableColor.Color.R;
        s.TableColorG = _tableColor.Color.G;
        s.TableColorB = _tableColor.Color.B;
        s.TableColorA = _tableColor.Color.A;
        s.RotationStepIndex = _rotationStep.Selected;

        s.AllowObservers = _observerToggle.ButtonPressed;
        s.MaxPlayers = ParseInt(_maxPlayers.Text, s.MaxPlayers);

        s.Players.Clear();

        foreach (var c in _playerDefinitionContainer.GetChildren())
        {
            if (c is PlayerDefinition pd)
            {
                var i = pd.GetPlayerInfo();
                s.Players.Add(
                    new ProjectPlayerSettings
                    {
                        Name = i.Item1,
                        ColorR = i.Item2.R,
                        ColorG = i.Item2.G,
                        ColorB = i.Item2.B,
                        ColorA = i.Item2.A,
                    }
                );
            }
        }

        // Game Info tab
        s.GameTitle = _gameTitle.Text;
        s.Designers = _designers.Text;
        s.GraphicDesign = _graphicDesign.Text;
        s.Artists = _artists.Text;
        s.ContactInfo = _contactInfo.Text;
        s.VisionStatement = _visionStatement.Text;

        ProjectService.Instance.SaveProject(project);
        EventBus.Instance.Publish<ProjectSettingsChangedEvent>();
        OnClosePressed();
    }

    private static float ParseFloat(string text, float fallback) =>
        float.TryParse(text, out var v) ? v : fallback;

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var v) ? v : fallback;
}

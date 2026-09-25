using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Godot;

public partial class ProjectSettingsDialog : Window
{
    /// <summary>
    /// The settings when the window was opened.
    /// Used to detect which fields were edited.
    /// </summary>
    private ProjectGameSettings _baseline;

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
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        var next = R.Value<ProjectGameSettings>();
        if (next == null)
            return;

        if (_baseline == null)
        {
            Load(next);
            return;
        }

        var ui = ReadUi();
        var merged = MergeSettings(_baseline, ui, next);
        WriteUiDiff(ui, merged);
        _baseline = next; // safe: immutable, no clone
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
                _playerDefinitionContainer.AddChild(newPd);
                newPd.SetPlayerInfo(
                    i + 1,
                    new ProjectPlayerSettings { Name = $"Player {i + 1}", Color = _playerColors[i] }
                );
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

    private void Load(ProjectGameSettings s)
    {
        _2dToggle.ButtonPressed = s.StartIn2D;
        _playerHandsToggle.ButtonPressed = s.EnablePlayerHands;
        _tableWidth.Text = s.TableWidth.ToString();
        _tableHeight.Text = s.TableHeight.ToString();
        _tableUnits.Selected = s.TableUnits;
        _tableColor.Color = s.TableColor;
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

        _baseline = s;
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
                    gameSettings.Players.Length > index
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

        ProjectService.Instance.UpdateGameSettings(ReadUi());
        OnClosePressed();
    }

    /// <summary>
    /// Reads every widget into a fresh settings record.
    /// </summary>
    private ProjectGameSettings ReadUi()
    {
        var current = ProjectService.Instance.Settings.Value;
        return current with
        {
            StartIn2D = _2dToggle.ButtonPressed,
            EnablePlayerHands = _playerHandsToggle.ButtonPressed,
            TableWidth = ParseFloat(_tableWidth.Text, current.TableWidth),
            TableHeight = ParseFloat(_tableHeight.Text, current.TableHeight),
            TableUnits = _tableUnits.Selected,
            TableColor = _tableColor.Color,
            RotationStepIndex = _rotationStep.Selected,
            AllowObservers = _observerToggle.ButtonPressed,
            MaxPlayers = ParseInt(_maxPlayers.Text, current.MaxPlayers),
            Players = ReadPlayersFromUi(current.Players),
            GameTitle = _gameTitle.Text,
            Designers = _designers.Text,
            GraphicDesign = _graphicDesign.Text,
            Artists = _artists.Text,
            ContactInfo = _contactInfo.Text,
            VisionStatement = _visionStatement.Text,
        };
    }

    /// <summary>
    /// Reads the seats from the UI, keeping each existing seat's hand container.
    /// </summary>
    private ImmutableArray<ProjectPlayerSettings> ReadPlayersFromUi(
        ImmutableArray<ProjectPlayerSettings> current
    )
    {
        var builder = ImmutableArray.CreateBuilder<ProjectPlayerSettings>();
        foreach (var c in _playerDefinitionContainer.GetChildren())
        {
            if (c is PlayerDefinition pd)
            {
                var (name, color, isAdmin) = pd.GetPlayerInfo();
                var seat = builder.Count;
                builder.Add(
                    new ProjectPlayerSettings
                    {
                        Name = name,
                        Color = color,
                        IsAdmin = isAdmin,
                        HandRef =
                            seat < current.Length
                                ? current[seat].HandRef
                                : Snowport.Clock.CreateTag(),
                    }
                );
            }
        }
        return builder.ToImmutable();
    }

    private static T Pick<T>(T ui, T baseline, T next) =>
        EqualityComparer<T>.Default.Equals(ui, baseline) ? next : ui;

    /// <summary>
    /// Three-way merge
    /// For each field, keep the UI value if the user edited it.
    /// Otherwise take the incoming value.
    /// </summary>
    public static ProjectGameSettings MergeSettings(
        ProjectGameSettings baseline,
        ProjectGameSettings ui,
        ProjectGameSettings next
    )
    {
        bool playersEdited =
            ui.MaxPlayers != baseline.MaxPlayers || !ui.Players.SequenceEqual(baseline.Players);

        return next with
        {
            StartIn2D = Pick(ui.StartIn2D, baseline.StartIn2D, next.StartIn2D),
            EnablePlayerHands = Pick(
                ui.EnablePlayerHands,
                baseline.EnablePlayerHands,
                next.EnablePlayerHands
            ),
            TableWidth = Pick(ui.TableWidth, baseline.TableWidth, next.TableWidth),
            TableHeight = Pick(ui.TableHeight, baseline.TableHeight, next.TableHeight),
            TableUnits = Pick(ui.TableUnits, baseline.TableUnits, next.TableUnits),
            TableColor = Pick(ui.TableColor, baseline.TableColor, next.TableColor),
            RotationStepIndex = Pick(
                ui.RotationStepIndex,
                baseline.RotationStepIndex,
                next.RotationStepIndex
            ),
            AllowObservers = Pick(ui.AllowObservers, baseline.AllowObservers, next.AllowObservers),
            GameTitle = Pick(ui.GameTitle, baseline.GameTitle, next.GameTitle),
            Designers = Pick(ui.Designers, baseline.Designers, next.Designers),
            GraphicDesign = Pick(ui.GraphicDesign, baseline.GraphicDesign, next.GraphicDesign),
            Artists = Pick(ui.Artists, baseline.Artists, next.Artists),
            ContactInfo = Pick(ui.ContactInfo, baseline.ContactInfo, next.ContactInfo),
            VisionStatement = Pick(
                ui.VisionStatement,
                baseline.VisionStatement,
                next.VisionStatement
            ),
            MaxPlayers = playersEdited ? ui.MaxPlayers : next.MaxPlayers,
            Players = playersEdited ? ui.Players : next.Players,
        };
    }

    /// <summary>
    /// Writes only the widgets whose target value differs from what they currently show.
    /// </summary>
    private void WriteUiDiff(ProjectGameSettings current, ProjectGameSettings target)
    {
        if (target.StartIn2D != current.StartIn2D)
            _2dToggle.ButtonPressed = target.StartIn2D;
        if (target.EnablePlayerHands != current.EnablePlayerHands)
            _playerHandsToggle.ButtonPressed = target.EnablePlayerHands;
        if (target.TableWidth != current.TableWidth)
            _tableWidth.Text = target.TableWidth.ToString();
        if (target.TableHeight != current.TableHeight)
            _tableHeight.Text = target.TableHeight.ToString();
        if (target.TableUnits != current.TableUnits)
            _tableUnits.Selected = target.TableUnits;
        if (target.TableColor != current.TableColor)
            _tableColor.Color = target.TableColor;
        if (target.RotationStepIndex != current.RotationStepIndex)
            _rotationStep.Selected = target.RotationStepIndex;
        if (target.AllowObservers != current.AllowObservers)
            _observerToggle.ButtonPressed = target.AllowObservers;
        if (target.GameTitle != current.GameTitle)
            _gameTitle.Text = target.GameTitle;
        if (target.Designers != current.Designers)
            _designers.Text = target.Designers;
        if (target.GraphicDesign != current.GraphicDesign)
            _graphicDesign.Text = target.GraphicDesign;
        if (target.Artists != current.Artists)
            _artists.Text = target.Artists;
        if (target.ContactInfo != current.ContactInfo)
            _contactInfo.Text = target.ContactInfo;
        if (target.VisionStatement != current.VisionStatement)
            _visionStatement.Text = target.VisionStatement;

        if (
            target.MaxPlayers != current.MaxPlayers
            || !target.Players.SequenceEqual(current.Players)
        )
        {
            _maxPlayers.Text = target.MaxPlayers.ToString();
            SetPlayers(target);
        }
    }

    private static float ParseFloat(string text, float fallback) =>
        float.TryParse(text, out var v) ? v : fallback;

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var v) ? v : fallback;
}

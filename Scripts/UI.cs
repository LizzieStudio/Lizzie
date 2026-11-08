using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class UI : CanvasLayer
{
    private HBoxContainer modeButtons;

    public event EventHandler<SceneModeChangeArgs> SceneModeChange;

    private ComponentDefinition _componentDefinition;
    private TemplateCreator _templateCreator;

    private Label _componentName;

    private DatasetEditor _datasetEditor;
    private PrototypeManifest _prototypeManifest;
    private MultiplayerDialog _multiplayerDialog;
    private ImageManager _imageManager;
    private PlayerPositionDialog _playerPositionDialog;

    private HandManager _handManager;
    private PlayerHandsPanel _opponentHands;
    private float _opponentHandsOpenOffsetLeft;
    private float _opponentHandsOpenOffsetRight;
    private float _opponentHandsHiddenOffsetLeft;
    private float _opponentHandsHiddenOffsetRight;
    private const float OpponentHandsAnimDuration = 0.25f;
    private const float OpponentHandsPanelMargin = 6f;
    private Tween _opponentHandsTween;

    private OptionButton _rotationStep;

    // the toggle buttons that pick the stage the view shows, one for each in the current table's chain
    private HBoxContainer _stages;
    private readonly ButtonGroup _stageGroup = new();

    // The style for the stage buttons.
    private readonly (string State, StyleBox Box)[] _blueprintStyles = StageStyles(isTable: false);
    private readonly (string State, StyleBox Box)[] _tableStyles = StageStyles(isTable: true);

    /// <summary>
    /// The color that marks a stage, green for a table and blue for a blueprint, at <paramref name="lightness"/>.
    /// </summary>
    public static Color StageColor(bool isTable, float lightness) =>
        Color.FromOkHsl(isTable ? 0.40f : 0.73f, 0.6f, lightness);

    private static (string State, StyleBox Box)[] StageStyles(bool isTable)
    {
        // The default button box, so only the color differs.
        StyleBox Box(string state, float lightness)
        {
            var box = (StyleBoxFlat)
                ThemeDB.GetDefaultTheme().GetStylebox(state, "Button").Duplicate();
            box.BgColor = StageColor(isTable, lightness);
            return box;
        }
        var pressed = Box("pressed", 0.42f);
        return
        [
            ("normal", Box("normal", 0.25f)),
            ("hover", Box("hover", 0.32f)),
            ("pressed", pressed),
            ("hover_pressed", pressed),
        ];
    }

    private BlueprintManager _blueprintManager;
    private SeatsWindow _seats;

    private Node _modalDialogs;

    public override void _EnterTree()
    {
        _instance = this;
        Atoms.R.Watch(this, Sync);
        Atoms.R.Watch(this, SyncStages);
        Atoms.R.Watch(this, PromptIfSeatRemoved);
    }

    // A copy of the local player's seat to detect changes for reprompting them.
    private Ref<Seat> _currentSeat;

    private void PromptIfSeatRemoved(IWatchReader R)
    {
        var seat = R.Get(Seating.Only).Seats.GetValueOrDefault(Source.Me);
        var wasRemoved = R.Get(Seat.ById, seat) is { Deleted: true };
        if (wasRemoved && seat != _currentSeat)
            ShowPlayerSeatingDialog();
        _currentSeat = wasRemoved ? seat : Ref<Seat>.None;
    }

    private void SyncStages(IWatchReader R)
    {
        foreach (var old in _stages.GetChildren())
        {
            _stages.RemoveChild(old);
            old.QueueFree();
        }

        var current = R.Get(CurrentTable.Only).StageRef;
        var shown = ProjectService.Instance.GameObjects.ShownStage(R);
        foreach (var stage in ProjectService.Instance.GameObjects.PickableStages(R))
        {
            // Picking the current table follows it, even when another becomes current.
            var pick = stage.Id == current ? Ref<Stage>.None : stage.Id;
            var button = new Button
            {
                Text = stage.Name,
                ToggleMode = true,
                ButtonGroup = _stageGroup,
                ButtonPressed = stage.Id == shown,
            };
            foreach (var (state, box) in stage.IsTable ? _tableStyles : _blueprintStyles)
                button.AddThemeStyleboxOverride(state, box);
            button.Pressed += () => ProjectService.Instance.GameObjects.PickedStageRef.Value = pick;
            _stages.AddChild(button);
        }
    }

    private void Sync(IWatchReader R)
    {
        var s = R.Get(ProjectGameSettings.Only);
        HandManager.Visible = s.EnablePlayerHands;
        _opponentHands.Visible = s.EnablePlayerHands;
        _rotationStep.Selected = s.RotationStepIndex;
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        // don't close automatically in case their are unsaved changes
        GetTree().AutoAcceptQuit = false;

        modeButtons = GetNode<HBoxContainer>("Mode");

        SetSceneMode(Config.Registry.Get<SceneMode>("SceneMode"));

        _rotationStep = GetNode<OptionButton>("%RotationStep");
        _rotationStep.ItemSelected += RotationStepSelected;

        _stages = GetNode<HBoxContainer>("%Stages");

        var viewOptions = GetNode<MenuButton>("%ViewOptions").GetPopup();
        viewOptions.HideOnCheckableItemSelection = false;
        viewOptions.SetItemTooltip(0, "Shows components from parent blueprints that were removed.");
        viewOptions.SetItemTooltip(1, "Marks the components that would be deleted by a reset.");
        viewOptions.IndexPressed += index =>
        {
            int i = (int)index;
            viewOptions.ToggleItemChecked(i);
            bool on = viewOptions.IsItemChecked(i);
            if (i == 0)
                Atoms.R.ShowsRemoved = on;
            else
                ProjectService.Instance.GameObjects.MarksTemporary.Value = on;
        };

        _componentName = GetNode<Label>("%ComponentName");

        _modalDialogs = GetNode("%ModalDialogs");

        _handManager = GetNode<HandManager>("%Hand");
        _opponentHands = GetNode<PlayerHandsPanel>("%PlayerHandsPanel");
        _opponentHands.IsShowingChanged += OnOpponentHandsShowHideToggled;

        // Defer position capture until layout is resolved.
        CallDeferred(nameof(InitOpponentHandsPositions));

        EventBus.Instance.Subscribe<EditPrototypeEvent>(ShowComponentEditDialog);
        EventBus.Instance.Subscribe<ShowTemplateEditor>(ShowTemplateEditorFromEvent);
        EventBus.Instance.Subscribe<ShowDatasetEditor>(ShowDatasetEditorFromEvent);
        EventBus.Instance.Subscribe<ShowImageManagerEvent>(ShowImageManagerFromEvent);
        EventBus.Instance.Subscribe<ShowComponentPreviewDialogEvent>(ShowComponentPreviewDialog);
        EventBus.Instance.Subscribe<RequestPlayerPositionEvent>(OnRequestPlayerPosition);
        ProjectService.Instance.ProjectReplaced += CloseProjectWindows;
    }

    /// <summary>
    /// Closes the windows open on the project, like the editors, which would still show the one replaced.
    /// </summary>
    /// <remarks>
    /// This is a stopgap and could be replaced at some point.
    /// In the future I want to reload the whole scene to catch everything.
    /// </remarks>
    private void CloseProjectWindows()
    {
        foreach (var window in _modalDialogs.GetChildren().OfType<Window>())
            if (window is not (ProjectManager or MultiplayerDialog or AcceptDialog))
                window.QueueFree();
    }

    private void InitOpponentHandsPositions()
    {
        if (_opponentHands == null)
            return;

        _opponentHandsOpenOffsetLeft = _opponentHands.OffsetLeft;
        _opponentHandsOpenOffsetRight = _opponentHands.OffsetRight;

        // Slide the whole panel right so only the button tab remains on screen.
        // Moving both offsets by the same delta keeps the panel width constant,
        // so the minimum-size constraint never interferes.
        var btn = _opponentHands.GetNode<Button>("%ShowHideButton");
        float visibleWidth = btn.Size.X + OpponentHandsPanelMargin * 2;
        float slideAmount = _opponentHands.Size.X - visibleWidth;

        _opponentHandsHiddenOffsetLeft = _opponentHandsOpenOffsetLeft + slideAmount;
        _opponentHandsHiddenOffsetRight = _opponentHandsOpenOffsetRight + slideAmount;
    }

    private void OnOpponentHandsShowHideToggled(object sender, bool isShowing)
    {
        if (_opponentHands == null)
            return;

        float targetLeft = isShowing
            ? _opponentHandsOpenOffsetLeft
            : _opponentHandsHiddenOffsetLeft;
        float targetRight = isShowing
            ? _opponentHandsOpenOffsetRight
            : _opponentHandsHiddenOffsetRight;

        _opponentHandsTween?.Kill();
        _opponentHandsTween = _opponentHands.CreateTween();
        _opponentHandsTween
            .TweenProperty(_opponentHands, "offset_left", targetLeft, OpponentHandsAnimDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _opponentHandsTween
            .Parallel()
            .TweenProperty(_opponentHands, "offset_right", targetRight, OpponentHandsAnimDuration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    private void OnRequestPlayerPosition(RequestPlayerPositionEvent _)
    {
        ShowPlayerSeatingDialog();
    }

    private void ShowPlayerSeatingDialog()
    {
        if (BroughtForward(_playerPositionDialog))
            return;

        string s = "res://Scenes/Controls/player_position_dialog.tscn";

        _playerPositionDialog = GD.Load<PackedScene>(s).Instantiate<PlayerPositionDialog>();
        _modalDialogs.AddChild(_playerPositionDialog);
        _playerPositionDialog.ShowCentered();
    }

    private void ShowComponentPreviewDialog(ShowComponentPreviewDialogEvent obj)
    {
        string s = "res://Scenes/Controls/ComponentPreviewPopup.tscn";
        var popup = GD.Load<PackedScene>(s).Instantiate<ComponentPreviewPopup>();
        _modalDialogs.AddChild(popup);

        popup.ShowComponent(obj.Component);
    }

    private void RotationStepSelected(long index)
    {
        var settings = Atoms.R.Get(ProjectGameSettings.Only);
        Atoms.R.Write(this, settings with { RotationStepIndex = (int)index });
    }

    // Brings the window forward if it's open, so its command never opens a second one.
    // False when it isn't open.
    private static bool BroughtForward(Window window)
    {
        if (!IsInstanceValid(window) || window.IsQueuedForDeletion())
            return false;
        window.GrabFocus();
        return true;
    }

    private void ShowComponentDefinition()
    {
        if (BroughtForward(_componentDefinition))
            return;
        var s = "res://Scenes/ComponentPanels/component_definition.tscn";
        _componentDefinition = GD.Load<PackedScene>(s).Instantiate<ComponentDefinition>();
        _modalDialogs.AddChild(_componentDefinition);

        _componentDefinition.Initialize(ProjectService.Instance.CurrentProject);
    }

    private void ShowPrototypeManifest()
    {
        if (BroughtForward(_prototypeManifest))
            return;
        var s = "res://Scenes/Prototypes/PrototypeManifest.tscn";
        _prototypeManifest = GD.Load<PackedScene>(s).Instantiate<PrototypeManifest>();
        _modalDialogs.AddChild(_prototypeManifest);
    }

    private void ShowTemplateEditor()
    {
        ShowTemplateEditorFromEvent(new ShowTemplateEditor { TemplateRef = Ref<Template>.None });
    }

    private void ShowTemplateEditorFromEvent(ShowTemplateEditor e)
    {
        if (!BroughtForward(_templateCreator))
        {
            string s = "res://Scenes/Templating/TemplateCreator.tscn";
            _templateCreator = GD.Load<PackedScene>(s).Instantiate<TemplateCreator>();
            _modalDialogs.AddChild(_templateCreator);
        }
        _templateCreator.SetTemplateById(e.TemplateRef);
    }

    private void ShowDatasetEditor()
    {
        ShowDatasetEditorFromEvent(new ShowDatasetEditor { DatasetRef = Ref<Dataset>.None });
    }

    private void ShowDatasetEditorFromEvent(ShowDatasetEditor e)
    {
        if (!BroughtForward(_datasetEditor))
        {
            string s = "res://Scenes/Dataset/DatasetEditor.tscn";
            _datasetEditor = GD.Load<PackedScene>(s).Instantiate<DatasetEditor>();
            _modalDialogs.AddChild(_datasetEditor);
        }
        if (!e.DatasetRef.IsNone)
            _datasetEditor.ShownDatasetRef.Value = e.DatasetRef;
        if (!e.SelectedRowRef.IsNone)
            _datasetEditor.RevealRow(e.SelectedRowRef);
    }

    private void ShowImageManager()
    {
        ShowImageManagerFromEvent(new ShowImageManagerEvent());
    }

    private void ShowProjectSettings()
    {
        if (BroughtForward(_projectSettings))
            return;
        string s = "res://Scenes/Project/ProjectSettings.tscn";
        _projectSettings = GD.Load<PackedScene>(s).Instantiate<ProjectSettingsDialog>();
        _modalDialogs.AddChild(_projectSettings);
    }

    private void ShowSeats()
    {
        if (BroughtForward(_seats))
            return;
        var s = "res://Scenes/Project/SeatsWindow.tscn";
        _seats = GD.Load<PackedScene>(s).Instantiate<SeatsWindow>();
        _modalDialogs.AddChild(_seats);
    }

    private void ShowBlueprintManager()
    {
        if (BroughtForward(_blueprintManager))
            return;
        var s = "res://Scenes/Project/BlueprintManager.tscn";
        _blueprintManager = GD.Load<PackedScene>(s).Instantiate<BlueprintManager>();
        _modalDialogs.AddChild(_blueprintManager);
    }

    private void ShowImageManagerFromEvent(ShowImageManagerEvent e)
    {
        if (!BroughtForward(_imageManager))
        {
            string s = "res://Scenes/Controls/ImageManager.tscn";
            _imageManager = GD.Load<PackedScene>(s).Instantiate<ImageManager>();
            _modalDialogs.AddChild(_imageManager);
        }
        if (!e.ImageRef.IsNone)
            _imageManager.Open(e.ImageRef);
    }

    /// <summary>
    /// Tells the player about a problem, like one opening or saving a project.
    /// </summary>
    /// <param name="host">What the message opens over.</param>
    public static void ShowMessage(Node host, string title, string message)
    {
        var dialog = new AcceptDialog
        {
            Title = title,
            DialogText = message,
            DialogAutowrap = true,
            Exclusive = false,
        };
        dialog.GetLabel().CustomMinimumSize = new Vector2(480, 0);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        host.AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Opens the project opened last, or the project manager when there's none or it can't open.
    /// </summary>
    public void OpenLastProject()
    {
        var last = ProjectLibrary.LastOpened;
        string error = null;
        if (System.IO.Directory.Exists(last) && TryOpenProject(last, out error))
            return;
        ProjectService.Instance.NewGame();
        ShowProjectManager();
        // Over the manager, or the manager would hide it.
        if (error != null)
            ShowMessage(_projectManager, "Can't Open Project", error);
    }

    private bool TryOpenProject(string folder, out string error)
    {
        error = null;
        try
        {
            ProjectService.Instance.OpenProject(folder, out var warning);
            if (warning != null)
                ShowMessage(_modalDialogs, "Older Save Opened", warning);
            return true;
        }
        catch (ProjectLoadException e)
        {
            error = e.Message;
            return false;
        }
    }

    private void OpenFromProjectManager(string folder)
    {
        bool managerOpen =
            IsInstanceValid(_projectManager) && !_projectManager.IsQueuedForDeletion();
        PromptToCloseProject(
            $"Opening \"{ProjectFolder.NameOf(folder)}\"",
            "Open",
            leavesGame: true,
            managerOpen ? _projectManager : _modalDialogs,
            () =>
            {
                if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
                    MultiplayerManager.Instance.Disconnect();
                if (!TryOpenProject(folder, out var error))
                    ShowMessage(_modalDialogs, "Can't Open Project", error);
                else if (IsInstanceValid(_projectManager))
                    _projectManager.QueueFree();
            }
        );
    }

    private void Save()
    {
        var current = ProjectService.Instance.CurrentProject;
        if (current != null && current.Folder == null)
            ShowSaveAsDialog();
        else
            TrySaveProject(_modalDialogs);
    }

    private static bool TrySaveProject(Node host)
    {
        if (ProjectService.Instance?.SaveProject() == true)
            return true;
        var folder = ProjectService.Instance?.CurrentProject?.Folder;
        ShowMessage(
            host,
            "Can't Save Project",
            $"Lizzie couldn't save to \"{folder}\". The folder may have been moved or deleted, "
                + "or something else may be using it. Use Save Project As to save it somewhere else."
        );
        return false;
    }

    /// <summary>
    /// Asks for a name and location, then saves the project into a new folder there and keeps saving to it.
    /// </summary>
    /// <param name="onSaved">Runs once it's saved.</param>
    /// <param name="host">What the dialogs open over.</param>
    private void ShowSaveAsDialog(Action onSaved = null, Node host = null)
    {
        host ??= _modalDialogs;
        var dialog = ProjectNameDialog.CreateForNew(
            "Save Project As",
            "Save",
            ProjectLibrary.Location
        );
        dialog.OnChosen += (location, name) =>
        {
            string folder;
            try
            {
                folder = ProjectFolder.Create(location, name);
            }
            catch (Exception e) when (ProjectFolder.IsFileError(e))
            {
                ShowMessage(
                    host,
                    "Can't Save Project",
                    $"Lizzie couldn't make a folder for \"{name}\".\n\n{e.Message}"
                );
                return;
            }
            ProjectLibrary.Location = location;
            if (!ProjectService.Instance.SaveProjectAs(folder))
            {
                // Nothing was saved, so the folder it made goes too.
                try
                {
                    System.IO.Directory.Delete(folder);
                }
                catch (Exception e) when (ProjectFolder.IsFileError(e)) { }
                ShowMessage(host, "Can't Save Project", $"Lizzie couldn't save into \"{folder}\".");
                return;
            }
            onSaved?.Invoke();
        };
        host.AddChild(dialog);
        dialog.Open();
    }

    /// <summary>
    /// On closing the project, prompt the user if necessary, then call <paramref name="done"/>.
    /// </summary>
    /// <param name="reason">What closes it, like "Opening \"My Game\"", for the questions.</param>
    /// <param name="okText">The button that goes ahead when there's nothing to save, like "Open".</param>
    /// <param name="leavesGame">True when <paramref name="done"/> leaves or ends the multiplayer game.</param>
    /// <param name="host">What the dialogs open over.</param>
    private void PromptToCloseProject(
        string reason,
        string okText,
        bool leavesGame,
        Node host,
        Action done
    )
    {
        var unsaved = ProjectService.Instance?.HasUnsavedChanges == true;
        var game = leavesGame ? LeavingGameWarning(reason) : null;
        if (!unsaved && game == null)
        {
            done();
            return;
        }

        var lines = new List<string>();
        if (game != null)
            lines.Add(game);
        if (unsaved)
            lines.Add(
                $"This project has unsaved changes. Save before {char.ToLowerInvariant(reason[0])}{reason[1..]}?"
            );
        var dialog = new ConfirmationDialog
        {
            Title = unsaved ? "Unsaved Changes" : "Leave Game",
            DialogText = string.Join("\n\n", lines),
            DialogAutowrap = true,
            OkButtonText = unsaved ? "Save" : okText,
        };
        dialog.GetLabel().CustomMinimumSize = new Vector2(380, 0);
        if (unsaved)
            dialog.AddButton("Don't Save", true, "discard");

        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            if (unsaved)
                SaveThen(done, host);
            else
                PromptToCloseProject(reason, okText, leavesGame: false, host, done);
        };
        dialog.CustomAction += action =>
        {
            if (action != "discard")
                return;
            dialog.QueueFree();
            done();
        };
        dialog.Canceled += dialog.QueueFree;

        host.AddChild(dialog);
        dialog.PopupCentered();
    }

    private static string LeavingGameWarning(string doing)
    {
        var multiplayer = MultiplayerManager.Instance;
        if (multiplayer?.IsMultiplayerActive != true)
            return null;
        return multiplayer.IsServer
            ? $"{doing} will end the game you're hosting."
            : $"{doing} will leave the game you joined.";
    }

    private void SaveThen(Action then, Node host)
    {
        if (ProjectService.Instance?.CurrentProject is { Folder: null })
            ShowSaveAsDialog(then, host);
        else if (TrySaveProject(host))
            then();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            HandleCloseRequest();
    }

    private void HandleCloseRequest() =>
        PromptToCloseProject(
            "Closing",
            "Quit",
            leavesGame: false,
            _modalDialogs,
            () => GetTree().Quit()
        );

    private ProjectManager _projectManager;

    public void ShowProjectManager()
    {
        if (BroughtForward(_projectManager))
            return;
        var s = "res://Scenes/Project/ProjectManager.tscn";
        _projectManager = GD.Load<PackedScene>(s).Instantiate<ProjectManager>();
        _projectManager.OpenRequested += OpenFromProjectManager;
        _modalDialogs.AddChild(_projectManager);
    }

    private void ShowMultiplayerDialog()
    {
        if (BroughtForward(_multiplayerDialog))
            return;

        var d = GD.Load<PackedScene>("res://Scenes/Controls/multiplayer_connect.tscn");
        _multiplayerDialog = d.Instantiate<MultiplayerDialog>();
        _modalDialogs.AddChild(_multiplayerDialog);
        _multiplayerDialog.PopupCentered();
    }

    private void ShowComponentEditDialog(EditPrototypeEvent editEvent)
    {
        var p = Atoms.R.Get(Prototype.ById, editEvent.PrototypeRef);
        if (p == null)
        {
            return;
        }

        var s = "res://Scenes/ComponentPanels/component_definition.tscn";
        var editPanel = GD.Load<PackedScene>(s).Instantiate<ComponentDefinition>();
        _modalDialogs.AddChild(editPanel);

        editPanel.SetEditMode();
        editPanel.Initialize(ProjectService.Instance.CurrentProject);
        editPanel.DisplayPrototype(p);
    }

    private ProjectSettingsDialog _projectSettings;

    private void SetSceneMode(SceneMode mode)
    {
        var buttons = modeButtons.GetChildren();
        foreach (var i in buttons)
        {
            if (i is Button b)
                b.Visible = true;
        }

        var buttonNum = 0;

        switch (mode)
        {
            case SceneMode.TwoD:
                buttonNum = 0;
                break;
            case SceneMode.ThreeD:
                buttonNum = 1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        if (buttons[buttonNum] is Button target)
            target.Visible = false;

        Config.Registry.Set("SceneMode", mode);
        SceneModeChange?.Invoke(this, new SceneModeChangeArgs { NewMode = mode });
    }

    private void _on_play_2d_pressed()
    {
        SetSceneMode(SceneMode.TwoD);
    }

    private void _on_play_3d_pressed()
    {
        SetSceneMode(SceneMode.ThreeD);
    }

    public void UpdateHoveredName(VisualComponentBase component)
    {
        if (component == null)
        {
            _componentName.Text = string.Empty;
            return;
        }

        _componentName.Text = component.ComponentName;
    }

    public HandManager HandManager => _handManager;
}

public class SceneModeChangeArgs : EventArgs
{
    public SceneMode NewMode { get; set; }
}

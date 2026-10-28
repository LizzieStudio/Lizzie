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

    private TextureFactory _textureFactory;

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
        var seat = R.Get(Seating.Only).Seats.GetValueOrDefault(Snowport.Clock.source);
        var wasRemoved = R.Get(Seat.ById, seat) is { Deleted: true };
        if (wasRemoved && seat != _currentSeat)
            ShowPlayerSeatingDialog();
        _currentSeat = wasRemoved ? seat : Ref<Seat>.Empty;
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
            var pick = stage.Id == current ? Ref<Stage>.Empty : stage.Id;
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

        GetNode<CheckButton>("%MarkReset").Toggled += on =>
            ProjectService.Instance.GameObjects.MarksTemporary.Value = on;

        _componentName = GetNode<Label>("%ComponentName");

        _textureFactory = GetNode<TextureFactory>("%TextureFactory");

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

        popup.ShowComponent(obj.Component, _textureFactory);
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

        _componentDefinition.SetTextureFactory(_textureFactory);
        _componentDefinition.Initialize(ProjectService.Instance.CurrentProject);
    }

    private void ShowPrototypeManifest()
    {
        if (BroughtForward(_prototypeManifest))
            return;
        var s = "res://Scenes/Prototypes/PrototypeManifest.tscn";
        _prototypeManifest = GD.Load<PackedScene>(s).Instantiate<PrototypeManifest>();
        _prototypeManifest.TextureFactory = _textureFactory;

        _modalDialogs.AddChild(_prototypeManifest);
    }

    private void ShowTemplateEditor()
    {
        ShowTemplateEditorFromEvent(new ShowTemplateEditor { TemplateRef = Ref<Template>.Empty });
    }

    private void ShowTemplateEditorFromEvent(ShowTemplateEditor e)
    {
        if (!BroughtForward(_templateCreator))
        {
            string s = "res://Scenes/Templating/TemplateCreator.tscn";
            _templateCreator = GD.Load<PackedScene>(s).Instantiate<TemplateCreator>();
            _templateCreator.TextureFactory = _textureFactory;
            _modalDialogs.AddChild(_templateCreator);
        }
        _templateCreator.SetTemplateById(e.TemplateRef);
    }

    private void ShowDatasetEditor()
    {
        ShowDatasetEditorFromEvent(new ShowDatasetEditor { DatasetRef = Ref<Dataset>.Empty });
    }

    private void ShowDatasetEditorFromEvent(ShowDatasetEditor e)
    {
        if (!BroughtForward(_datasetEditor))
        {
            string s = "res://Scenes/Dataset/DatasetEditor.tscn";
            _datasetEditor = GD.Load<PackedScene>(s).Instantiate<DatasetEditor>();
            _modalDialogs.AddChild(_datasetEditor);
        }
        if (!e.DatasetRef.IsEmpty)
            _datasetEditor.ShownDatasetRef.Value = e.DatasetRef;
        if (!e.SelectedRowRef.IsEmpty)
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
        if (!e.ImageRef.IsEmpty)
            _imageManager.Open(e.ImageRef);
    }

    /// <summary>
    /// Tells the player about a problem opening a project.
    /// </summary>
    public void ShowLoadMessage(string title, string message)
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
        _modalDialogs.AddChild(dialog);
        dialog.PopupCentered();
    }

    private void Save()
    {
        var current = ProjectService.Instance.CurrentProject;
        if (current != null && string.IsNullOrWhiteSpace(current.Filename))
            ShowSaveAsDialog();
        else
            ProjectService.Instance.SaveProject();
    }

    private void ShowSaveAsDialog(Action onSaved = null)
    {
        var dialog = new ConfirmationDialog { Title = "Save Project As", OkButtonText = "Save" };

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };

        var nameLabel = new Label { Text = "Project name:" };
        vbox.AddChild(nameLabel);

        var input = new LineEdit { PlaceholderText = "Enter project name..." };
        vbox.AddChild(input);

        dialog.AddChild(vbox);

        dialog.Confirmed += () =>
        {
            var name = input.Text.Trim();
            var project = ProjectService.Instance.CurrentProject;
            var saved = false;
            if (!string.IsNullOrEmpty(name) && project != null)
            {
                project.Filename = name;
                saved = ProjectService.Instance.SaveProject();
            }
            dialog.QueueFree();
            if (saved)
                onSaved?.Invoke();
        };
        dialog.Canceled += () => dialog.QueueFree();

        _modalDialogs.AddChild(dialog);
        dialog.PopupCentered();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            HandleCloseRequest();
    }

    /// <summary>
    /// On window close, confirm before discarding unsaved changes.
    /// </summary>
    private void HandleCloseRequest()
    {
        if (ProjectService.Instance?.HasUnsavedChanges != true)
        {
            GetTree().Quit();
            return;
        }

        var dialog = new ConfirmationDialog
        {
            Title = "Unsaved Changes",
            DialogText = "This project has unsaved changes. Save before closing?",
            OkButtonText = "Save",
            CancelButtonText = "Cancel",
        };
        dialog.AddButton("Don't Save", true, "discard");

        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            SaveThenQuit();
        };
        dialog.CustomAction += action =>
        {
            if (action == "discard")
            {
                dialog.QueueFree();
                GetTree().Quit();
            }
        };
        dialog.Canceled += () => dialog.QueueFree();

        _modalDialogs.AddChild(dialog);
        dialog.PopupCentered();
    }

    private void SaveThenQuit()
    {
        var project = ProjectService.Instance?.CurrentProject;
        if (project != null && string.IsNullOrWhiteSpace(project.Filename))
        {
            // don't quit unless the save succeeds
            ShowSaveAsDialog(onSaved: () => GetTree().Quit());
            return;
        }

        if (ProjectService.Instance?.SaveProject() == true)
            GetTree().Quit();
    }

    private void ShowProjectManager()
    {
        var s = "res://Scenes/Project/project_manager.tscn";
        _modalDialogs.AddChild(GD.Load<PackedScene>(s).Instantiate<ProjectManager>());
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
        editPanel.SetTextureFactory(_textureFactory);
        editPanel.Initialize(ProjectService.Instance.CurrentProject);
        editPanel.DisplayPrototype(p);
    }

    private ProjectSettingsDialog _projectSettings;

    private void SetSceneMode(SceneMode mode)
    {
        GD.Print($"Set Master Mode {mode}");
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
            case SceneMode.ThreeDFixed:
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
        SetSceneMode(SceneMode.ThreeDFixed);
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

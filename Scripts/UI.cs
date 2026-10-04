using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Godot;

public partial class UI : CanvasLayer
{
    [Export]
    private Color highlightFontColor;

    [Export]
    private Color baseFontColor;

    private HBoxContainer modeButtons;

    public event EventHandler<SceneModeChangeArgs> SceneModeChange;

    private ComponentDefinition _componentDefinition;
    private TemplateCreator _templateCreator;

    private Label _componentName;

    private GameController _gameController;
    private GameObjects _gameObjects;

    private TabContainer _componentTabs;

    private ProjectManager _projectManager;

    private TextureFactory _textureFactory;

    private DatasetEditor _datasetEditor;
    private PrototypeManifest _prototypeManifest;
    private MultiplayerDialog _multiplayerDialog;
    private ImageManager _imageManager;
    private ComponentPreviewPopup _componentPreviewPopup;
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

    private Node _modalDialogs;

    public override void _EnterTree()
    {
        _instance = this;
        RecordService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        var s = R.Single<ProjectGameSettings>();
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
        var buttons = modeButtons.GetChildren();
        baseFontColor = new Color(1, 1, 1, 1);

        SetSceneMode(Config.Registry.Get<SceneMode>("SceneMode"));

        _rotationStep = GetNode<OptionButton>("%RotationStep");
        _rotationStep.ItemSelected += RotationStepSelected;

        _componentName = GetNode<Label>("%ComponentName");

        _textureFactory = GetNode<TextureFactory>("%TextureFactory");

        _modalDialogs = GetNode("%ModalDialogs");

        _handManager = GetNode<HandManager>("%Hand");
        _opponentHands = GetNode<PlayerHandsPanel>("%PlayerHandsPanel");
        _opponentHands.ShowHideToggled += OnOpponentHandsShowHideToggled;

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

    private void OnOpponentHandsShowHideToggled(object sender, bool isHidden)
    {
        if (_opponentHands == null)
            return;

        float targetLeft = isHidden ? _opponentHandsHiddenOffsetLeft : _opponentHandsOpenOffsetLeft;
        float targetRight = isHidden
            ? _opponentHandsHiddenOffsetRight
            : _opponentHandsOpenOffsetRight;

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
        ShowPlayerPositionDialog();
    }

    private void ShowPlayerPositionDialog()
    {
        var settings = RecordService.Instance.Single<ProjectGameSettings>();

        // Don't open if there are no player slots and observers are not allowed.
        if (settings.Players.Length == 0 && !settings.AllowObservers)
            return;

        string s = "res://Scenes/Controls/player_position_dialog.tscn";

        _playerPositionDialog = GD.Load<PackedScene>(s).Instantiate<PlayerPositionDialog>();
        _modalDialogs.AddChild(_playerPositionDialog);
        _playerPositionDialog.ShowCentered();
    }

    private void ShowComponentPreviewDialog(ShowComponentPreviewDialogEvent obj)
    {
        string s = "res://Scenes/Controls/ComponentPreviewPopup.tscn";
        _componentPreviewPopup = GD.Load<PackedScene>(s).Instantiate<ComponentPreviewPopup>();
        _componentPreviewPopup.CloseDialog += ComponentPreviewPopupOnClosed;
        _modalDialogs.AddChild(_componentPreviewPopup);

        _componentPreviewPopup.ShowComponent(obj.Component, _textureFactory);
    }

    private void ComponentPreviewPopupOnClosed(object sender, EventArgs e)
    {
        _componentPreviewPopup.CloseDialog -= ComponentPreviewPopupOnClosed;
        _componentPreviewPopup.Hide();
        _componentPreviewPopup.QueueFree();
    }

    private void RotationStepSelected(long index)
    {
        var text = _rotationStep.GetItemText((int)index);

        // Strip off the last character (the degree symbol '°')
        if (!string.IsNullOrEmpty(text))
        {
            text = text.Substring(0, text.Length - 1);
            if (float.TryParse(text, out var step))
            {
                ProjectService.Instance.RotationStep = step;
            }
        }
    }

    /// <summary>Non-deleted snapshots in hierarchical order with depth.</summary>
    private static List<(GameState State, int Depth)> OrderedGameStates(IRecordReader R)
    {
        var result = new List<(GameState, int)>();

        var alive = R.Get<GameState>();
        var byParent = alive
            .GroupBy(s => s.Parent)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name).ToList());

        void Walk(SnowTag parent, int depth)
        {
            if (!byParent.TryGetValue(parent, out var kids))
                return;
            foreach (var k in kids)
            {
                result.Add((k, depth));
                Walk(k.Id, depth + 1);
            }
        }

        Walk(SnowTag.Empty, 0);

        var seen = result.Select(r => r.Item1.Id).ToHashSet();
        foreach (var s in alive.Where(s => !seen.Contains(s.Id)).OrderBy(s => s.Name))
            result.Add((s, 0));

        return result;
    }

    private static string GameStateLabel(IRecordReader R, GameState state)
    {
        var marker = state.Id == R.Single<ActiveGameStateRef>().GameStateId ? "● " : "";
        var parent = R.Get<GameState>(state.Parent);
        var parens = parent != null ? $" ({parent.Name})" : "";
        return marker + state.Name + parens;
    }

    private void ShowComponentDefinition()
    {
        var s = "res://Scenes/ComponentPanels/component_definition.tscn";
        _componentDefinition = GD.Load<PackedScene>(s).Instantiate<ComponentDefinition>();
        _componentDefinition.CreateObject += OnCreateObject;
        _componentDefinition.CancelDialog += OnCancelCreate;
        _modalDialogs.AddChild(_componentDefinition);

        _componentDefinition.SetTextureFactory(_textureFactory);
        _componentDefinition.Initialize(ProjectService.Instance.CurrentProject);
    }

    private void ShowPrototypeManifest()
    {
        var s = "res://Scenes/Prototypes/PrototypeManifest.tscn";
        _prototypeManifest = GD.Load<PackedScene>(s).Instantiate<PrototypeManifest>();
        _prototypeManifest.TextureFactory = _textureFactory;
        _prototypeManifest.Closed += PrototypeManifestOnClosed;

        _modalDialogs.AddChild(_prototypeManifest);
    }

    private void PrototypeManifestOnClosed(object sender, EventArgs e)
    {
        _prototypeManifest.Closed -= PrototypeManifestOnClosed;
        _prototypeManifest.Hide();
        _prototypeManifest.QueueFree();
    }

    private void ShowTemplateEditor()
    {
        ShowTemplateEditorFromEvent(new ShowTemplateEditor { TemplateRef = SnowTag.Empty });
    }

    private void ShowTemplateEditorFromEvent(ShowTemplateEditor e)
    {
        string s = "res://Scenes/Templating/TemplateCreator.tscn";
        _templateCreator = GD.Load<PackedScene>(s).Instantiate<TemplateCreator>();
        _templateCreator.TextureFactory = _textureFactory;
        _templateCreator.Closed += TemplateCreatorOnClosed;
        _modalDialogs.AddChild(_templateCreator);
        _templateCreator.SetTemplateById(e.TemplateRef);
    }

    private void TemplateCreatorOnClosed(object sender, EventArgs e)
    {
        _templateCreator.Closed -= TemplateCreatorOnClosed;
        _templateCreator.Hide();
        _templateCreator.QueueFree();
    }

    private void ShowDatasetEditor()
    {
        ShowDatasetEditorFromEvent(new ShowDatasetEditor { DatasetRef = SnowTag.Empty });
    }

    private void ShowDatasetEditorFromEvent(ShowDatasetEditor e)
    {
        string s = "res://Scenes/DataSet/DatasetEditor.tscn";
        _datasetEditor = GD.Load<PackedScene>(s).Instantiate<DatasetEditor>();
        _datasetEditor.Closed += DatasetEditorOnClosed;
        _modalDialogs.AddChild(_datasetEditor);
        _datasetEditor.SetDatasetById(e.DatasetRef);
        if (e.SelectedRowRef != SnowTag.Empty)
            _datasetEditor.RevealRow(e.SelectedRowRef);
    }

    private void DatasetEditorOnClosed(object sender, EventArgs e)
    {
        _datasetEditor.Closed -= DatasetEditorOnClosed;
        _datasetEditor.Hide();
        _datasetEditor.QueueFree();
    }

    private void ShowImageManager()
    {
        ShowImageManagerFromEvent(new ShowImageManagerEvent { ImageReference = Guid.Empty });
    }

    private void ShowProjectSettings()
    {
        string s = "res://Scenes/Project/ProjectSettings.tscn";
        _projectSettings = GD.Load<PackedScene>(s).Instantiate<ProjectSettingsDialog>();
        _projectSettings.Closed += ProjectSettingsOnClosed;
        _modalDialogs.AddChild(_projectSettings);
    }

    private void ProjectSettingsOnClosed(object sender, EventArgs e)
    {
        _projectSettings.Closed -= ProjectSettingsOnClosed;
        _projectSettings.Hide();
        _projectSettings.QueueFree();
    }

    private void ShowImageManagerFromEvent(ShowImageManagerEvent e)
    {
        string s = "res://Scenes/Controls/ImageManager.tscn";
        _imageManager = GD.Load<PackedScene>(s).Instantiate<ImageManager>();
        _imageManager.Closed += ImageManagerOnClosed;
        _modalDialogs.AddChild(_imageManager);
    }

    private void ImageManagerOnClosed(object sender, EventArgs e)
    {
        _imageManager.Closed -= ImageManagerOnClosed;
        _imageManager.Hide();
        _imageManager.QueueFree();
    }

    public void SetGameController(GameController gameController)
    {
        _gameController = gameController;
    }

    private void OpenSampleProject()
    {
        if (ProjectService.Instance.LoadProject(ProjectService.SampleProjectName) != null)
            ShowPlayerPositionDialog();
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

    private void ShowSaveSnapshotDialog()
    {
        var dialog = new ConfirmationDialog { Title = "Create Snapshot", OkButtonText = "Create" };

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(300, 0);

        var nameLabel = new Label();
        nameLabel.Text = "Snapshot name:";
        vbox.AddChild(nameLabel);

        var input = new LineEdit();
        input.PlaceholderText = "Enter snapshot name...";
        vbox.AddChild(input);

        var descLabel = new Label();
        descLabel.Text = "Description (optional):";
        vbox.AddChild(descLabel);

        var descInput = new TextEdit();
        descInput.CustomMinimumSize = new Vector2(300, 80);
        descInput.PlaceholderText = "Enter a description...";
        descInput.WrapMode = TextEdit.LineWrappingMode.Boundary;
        vbox.AddChild(descInput);

        var linkCheck = new CheckBox { ButtonPressed = false };
        vbox.AddChild(linkCheck);

        dialog.AddChild(vbox);

        dialog.Confirmed += () =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrEmpty(name))
                ProjectService.Instance.SaveGameState(
                    name,
                    descInput.Text.Trim(),
                    linkCheck.Visible && linkCheck.ButtonPressed
                );
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();

        _modalDialogs.AddChild(dialog);

        RecordService.Instance.Watch(
            dialog,
            R =>
            {
                var parent = R.Get<GameState>(R.Single<ActiveGameStateRef>().GameStateId);
                linkCheck.Visible = parent != null;
                if (parent == null)
                    return;
                linkCheck.Text = $"link to {parent.Name}";
                linkCheck.TooltipText =
                    $"When checked, this snapshot will copy updates to components in {parent.Name}.";
            }
        );

        dialog.PopupCentered();
    }

    private void ShowSnapshotManager()
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return;

        var dialog = new Window();
        dialog.Title = "Manage Snapshots";
        dialog.Size = new Vector2I(400, 300);
        dialog.Unresizable = false;

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 8;
        vbox.OffsetTop = 8;
        vbox.OffsetRight = -8;
        vbox.OffsetBottom = -8;
        dialog.AddChild(vbox);

        var list = new ItemList();
        list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(list);

        SnowTag SelectedRef()
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0)
                return SnowTag.Empty;
            return new SnowTag((int)list.GetItemMetadata(sel[0]));
        }

        var hbox = new HBoxContainer();
        vbox.AddChild(hbox);

        var restoreBtn = new Button();
        restoreBtn.Text = "Restore";
        restoreBtn.Pressed += () =>
        {
            var stateRef = SelectedRef();
            if (stateRef != SnowTag.Empty)
                ProjectService.Instance.SwitchGameState(stateRef);
        };
        hbox.AddChild(restoreBtn);

        var deleteBtn = new Button();
        deleteBtn.Text = "Delete";
        deleteBtn.Pressed += () =>
        {
            var stateRef = SelectedRef();
            if (stateRef == SnowTag.Empty)
                return;
            ProjectService.Instance.DeleteGameState(stateRef);
        };
        hbox.AddChild(deleteBtn);

        var closeBtn = new Button();
        closeBtn.Text = "Close";
        closeBtn.Pressed += () => dialog.QueueFree();
        hbox.AddChild(closeBtn);

        dialog.CloseRequested += () => dialog.QueueFree();

        _modalDialogs.AddChild(dialog);

        // Undo walks the snapshots and which one is active.
        CommandViews.Attach(dialog, record => record is GameState or ActiveGameStateRef);

        RecordService.Instance.Watch(
            dialog,
            R =>
            {
                var selected = SelectedRef();
                list.Clear();
                foreach (var (state, _) in OrderedGameStates(R))
                {
                    var idx = list.AddItem(GameStateLabel(R, state));
                    list.SetItemMetadata(idx, state.Id.Value);
                    if (state.Id == selected)
                        list.Select(idx);
                }
            }
        );

        dialog.PopupCentered();
    }

    private void ShowProjectManager()
    {
        var s = "res://Scenes/Project/project_manager.tscn";
        _projectManager = GD.Load<PackedScene>(s).Instantiate<ProjectManager>();
        _projectManager.Closed += ProjectManagerClosed;
        _modalDialogs.AddChild(_projectManager);
    }

    private void ProjectManagerClosed(object sender, EventArgs e)
    {
        _projectManager.Closed -= ProjectManagerClosed;
        _projectManager.QueueFree();
    }

    private void ShowMultiplayerDialog()
    {
        if (_multiplayerDialog != null && _multiplayerDialog.Visible)
        {
            // Dialog already open, just bring it to front
            _multiplayerDialog.MoveToForeground();
            return;
        }

        var d = GD.Load<PackedScene>("res://Scenes/Controls/multiplayer_connect.tscn");
        _multiplayerDialog = d.Instantiate<MultiplayerDialog>();
        _multiplayerDialog.CloseRequested += MultiplayerDialogClosed;
        _modalDialogs.AddChild(_multiplayerDialog);
        _multiplayerDialog.PopupCentered();
    }

    private void MultiplayerDialogClosed()
    {
        if (_multiplayerDialog != null)
        {
            _multiplayerDialog.CloseRequested -= MultiplayerDialogClosed;
            _multiplayerDialog.QueueFree();
            _multiplayerDialog = null;
        }
    }

    private ComponentDefinition _editPanel;
    private SnowTag _editingPrototypeId;

    private void ShowComponentEditDialog(EditPrototypeEvent editEvent)
    {
        var p = RecordService.Instance.GetIncludingDeleted<Prototype>(editEvent.PrototypeId);
        if (p == null)
        {
            return;
        }
        _editingPrototypeId = editEvent.PrototypeId;

        var s = "res://Scenes/ComponentPanels/component_definition.tscn";
        _editPanel = GD.Load<PackedScene>(s).Instantiate<ComponentDefinition>();
        _editPanel.CancelDialog += CloseComponentEditDialog;
        _editPanel.CloseDialog += CloseComponentEditDialog;
        _modalDialogs.AddChild(_editPanel);

        _editPanel.SetEditMode();
        _editPanel.SetTextureFactory(_textureFactory);
        _editPanel.Initialize(ProjectService.Instance.CurrentProject);
        _editPanel.DisplayPrototype(p);
    }

    private void CloseComponentEditDialog(object sender, EventArgs e)
    {
        _editPanel.Hide();
        _editPanel.QueueFree();
        _editingPrototypeId = SnowTag.Empty;
    }

    public override void _Process(double delta)
    {
        if (_modalDialogs == null)
        {
            ModalDialogShown = false;
        }
        else
        {
            ModalDialogShown = _modalDialogs.GetChildCount() > 0;
        }
    }

    /// <summary>
    /// Check to see if there are any modal dialogs open, and see if the flag needs to be flipped.
    /// If so, send the appropriate event signal.
    /// NOTE: All modal dialogs should be added as children to the ModalDialogs node for this to work properly. This is a bit hacky but it works for now and saves us from having to add event hooks for every single dialog we create.
    /// </summary>
    private bool _modalDialogShown;

    private bool ModalDialogShown
    {
        get => _modalDialogShown;
        set
        {
            if (value == _modalDialogShown)
                return;
            _modalDialogShown = value;

            if (value)
            {
                EventBus.Instance.Publish<ModalDialogOpenedEvent>();
            }
            else
            {
                EventBus.Instance.Publish<ModalDialogClosedEvent>();
            }
        }
    }

    private void OnInsertPressed()
    {
        _componentDefinition.Visible = true;
    }

    public event EventHandler<CreateObjectEventArgs> CreateObject;

    private void OnCreateObject(object sender, CreateObjectEventArgs args)
    {
        _componentDefinition.Visible = false;
        _componentDefinition.QueueFree();
        CreateObject?.Invoke(this, args);
    }

    private void OnCancelCreate(object sender, EventArgs e)
    {
        _componentDefinition.Visible = false;
        _componentDefinition.QueueFree();
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

    private void TextureTest()
    {
        var sv = GetNode<SubViewport>("SubViewport");
        var target = GetNode<TextureRect>("TestRect");

        var t = sv.GetTexture();
        target.Texture = t;
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

    public float HandY => _handManager.HandY;

    public HandManager HandManager => _handManager;
}

public class SceneModeChangeArgs : EventArgs
{
    public SceneMode NewMode { get; set; }
}

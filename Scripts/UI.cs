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

    private PopupMenu _editMenu;
    private PopupMenu _insertMenu;
    private PopupMenu _helpMenu;
    private PopupMenu _fileMenu;

    private PopupMenu _restoreSnapshotMenu;
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
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        var s = R.Value<ProjectGameSettings>();
        HandManager.Visible = s.EnablePlayerHands;
        _opponentHands.Visible = s.EnablePlayerHands;
        _rotationStep.Selected = s.RotationStepIndex;

        RebuildRestoreSnapshotMenu(R);
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

        _fileMenu = GetNode<PopupMenu>("%File");
        _fileMenu.AddSeparator();
        _fileMenu.AddItem("Create Snapshot", 3);
        _fileMenu.AddItem("Update Snapshot", 6);

        _restoreSnapshotMenu = new PopupMenu();
        _restoreSnapshotMenu.Name = "RestoreSnapshotMenu";
        _restoreSnapshotMenu.IdPressed += OnRestoreSnapshotSelected;
        _fileMenu.AddChild(_restoreSnapshotMenu);
        _fileMenu.AddSubmenuNodeItem("Restore Snapshot", _restoreSnapshotMenu, 4);

        _fileMenu.AddItem("Manage Snapshots...", 5);
        _fileMenu.AddSeparator();
        _fileMenu.AddItem("Multiplayer...", 10);
        _fileMenu.IdPressed += FileMenuOnIdPressed;

        _editMenu = GetNode<PopupMenu>("%Edit");
        _editMenu.AddItem("Templates", 1);
        _editMenu.AddItem("Datasets", 2);
        _editMenu.AddItem("Prototype Manifest", 3);
        _editMenu.AddItem("Images", 4);
        _editMenu.AddItem("Project Settings", 5);
        _editMenu.IdPressed += OnEditMenuSelection;

        _insertMenu = GetNode<PopupMenu>("%Insert");
        _insertMenu.AddItem("Existing Component", 1);
        _insertMenu.AddItem("New Component", 2);
        _insertMenu.AddItem("Zone", 3);
        _insertMenu.IdPressed += OnInsertMenuSelection;

        _helpMenu = GetNode<PopupMenu>("%Help");
        _helpMenu.AddItem("Test Function", 1);
        _helpMenu.IdPressed += OnHelpMenuSelection;

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
        var settings = ProjectService.Instance.Settings.Value;
        if (settings == null)
            return;

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

    private void RebuildRestoreSnapshotMenu(IRecordReader R)
    {
        _restoreSnapshotMenu.Clear();

        var updateIdx = _fileMenu.GetItemIndex(6);
        if (updateIdx >= 0)
            _fileMenu.SetItemDisabled(updateIdx, R.Value<ActiveGameStateRef>().Id == SnowTag.Empty);

        var ordered = OrderedGameStates(R);
        if (ordered.Count == 0)
        {
            _restoreSnapshotMenu.AddItem("(no snapshots)", -1);
            _restoreSnapshotMenu.SetItemDisabled(0, true);
            return;
        }

        foreach (var (state, _) in ordered)
            _restoreSnapshotMenu.AddItem(GameStateLabel(R, state), state.Id.Value);
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
        var marker = state.Id == R.Value<ActiveGameStateRef>().Id ? "● " : "";
        var parent = R.Get<GameState>(state.Parent);
        var parens = parent != null ? $" ({parent.Name})" : "";
        return marker + state.Name + parens;
    }

    private void OnRestoreSnapshotSelected(long id)
    {
        if (id < 0)
            return;
        ProjectService.Instance.SwitchGameState(new SnowTag((int)id));
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

    private void FileMenuOnIdPressed(long id)
    {
        switch (id)
        {
            case 0:
                ShowProjectManager();
                break;

            case 1:
                var p = ProjectService.Instance.LoadProject(ProjectService.SampleProjectName);
                if (p != null)
                {
                    ShowPlayerPositionDialog();
                }
                break;

            case 2:
                var current = ProjectService.Instance.CurrentProject;
                if (current != null && string.IsNullOrWhiteSpace(current.Filename))
                    ShowSaveAsDialog();
                else
                    ProjectService.Instance.SaveProject();
                break;

            case 3:
                ShowSaveSnapshotDialog();
                break;

            case 6:
                ProjectService.Instance.UpdateGameState(
                    ProjectService.Instance.ActiveGameState.Value.Id
                );
                break;

            // case 4 is handled by the _restoreSnapshotMenu submenu

            case 5:
                ShowSnapshotManager();
                break;

            case 10:
                ShowMultiplayerDialog();
                break;
        }
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

        ProjectService.Instance.Watch(
            dialog,
            R =>
            {
                var parent = R.Get<GameState>(R.Value<ActiveGameStateRef>().Id);
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
        CommandViews.Attach(
            dialog,
            fx =>
            {
                return fx
                    is UpdateReplicatedEffect<GameState>
                        or SetReplicatedValueEffect<ActiveGameStateRef>;
            }
        );
        dialog.TreeExiting += () => CommandViews.Detach(dialog);

        ProjectService.Instance.Watch(
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
        var p = ProjectService.Instance.GetIncludingDeleted<Prototype>(editEvent.PrototypeId);
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

    public bool ModalDialogShown
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

    private void ComponentPopupClosed()
    {
        GetParent<GameController>().ComponentPopupClosed();
    }

    // the open context menu, or null
    private PopupMenu _componentPopup;

    // Icons are drawn at text size, whatever size they're made at.
    private const int MenuIconSize = 20;

    /// <summary>
    /// Opens a context menu at <paramref name="position"/> with the commands for what was right-clicked.
    /// Each item shows its shortcut, and commands that ask for a number get a submenu.
    /// The menu is built for this opening and freed when it closes.
    /// </summary>
    public void ShowComponentPopup(Vector2I position, ICommandView view)
    {
        var context = view.BuildContext() ?? new CommandContext();
        // A menu still open is replaced. Only the current menu ends popup mode when it closes.
        _componentPopup?.QueueFree();
        var menu = new PopupMenu { Name = "ComponentPopup" };
        _componentPopup = menu;

        // the command for each item id, with what it acts on
        var items = new List<(Command Command, IReadOnlyList<Target> Targets)>();
        Type previousKind = null;
        foreach (var (command, targets) in CommandList.ForMenu(context))
        {
            // Commands on different kinds of target are separated.
            if (previousKind != null && command.GetType() != previousKind)
                menu.AddSeparator();
            previousKind = command.GetType();

            // Ids skip the separators, so they index the items.
            int id = items.Count;
            items.Add((command, targets));
            menu.AddItem(command.Label(targets.Count), id);
            int index = menu.GetItemIndex(id);

            if (command.ShortcutLabel() is { } shortcut)
                menu.SetItemShortcut(index, shortcut);

            if (command.Icon != null)
            {
                menu.SetItemIcon(index, GD.Load<Texture2D>(command.Icon));
                menu.SetItemIconMaxWidth(index, MenuIconSize);
            }

            if (command.AsksForNumber)
                AddNumberSubmenu(menu, command, targets, index, view);
        }

        menu.IdPressed += id =>
        {
            var (command, targets) = items[(int)id];
            // The item of a command that asks for a number only opens its submenu.
            if (!command.AsksForNumber)
                RunFromMenu(command, targets, 1, view);
        };
        menu.PopupHide += () =>
        {
            menu.QueueFree();
            if (_componentPopup != menu)
                return;
            _componentPopup = null;
            ComponentPopupClosed();
        };

        AddChild(menu);
        menu.Visible = true;
        // Fits the menu to its items before placing it.
        menu.ResetSize();

        // Opens away from the edges it would overflow, and stays in the window.
        var size = menu.Size;
        var window = (Vector2I)GetViewport().GetVisibleRect().Size;
        if (position.X + size.X > window.X)
            position.X -= size.X;
        if (position.Y + size.Y > window.Y)
            position.Y -= size.Y;
        menu.Position = position.Clamp(Vector2I.Zero, (window - size).Max(Vector2I.Zero));
    }

    // The submenu offers the top row of number keys, 1 through 9, unless the command gives its own limit.
    private const int SubmenuNumbers = 9;

    // It never offers more numbers than there are number keys, even with a higher limit.
    private const int MaxSubmenuNumbers = 20;

    // The submenu is built with its menu, so it runs its own command directly.
    private void AddNumberSubmenu(
        PopupMenu menu,
        Command command,
        IReadOnlyList<Target> targets,
        int index,
        ICommandView view
    )
    {
        var sub = new PopupMenu { Name = $"NumberSubmenu_{index}" };
        var numbers = new List<int>();
        int last = Math.Min(command.MaxNumber(targets) ?? SubmenuNumbers, MaxSubmenuNumbers);
        for (int n = 1; n <= last; n++)
        {
            sub.AddItem(n.ToString());
            numbers.Add(n);
            // The number keys run it with that number, so they show as its shortcuts.
            if (command.NumberLabel(n) is { } shortcut)
                sub.SetItemShortcut(sub.ItemCount - 1, shortcut);
        }
        if (command.InfiniteOption != null)
        {
            sub.AddItem(command.InfiniteOption);
            numbers.Add(int.MaxValue);
        }

        sub.IndexPressed += i => RunFromMenu(command, targets, numbers[(int)i], view);
        menu.AddChild(sub);
        menu.SetItemSubmenuNode(index, sub);
    }

    private void RunFromMenu(
        Command command,
        IReadOnlyList<Target> targets,
        int number,
        ICommandView view
    )
    {
        // Closed first, so a command can change the cursor mode, like Duplicate entering spawn mode.
        ComponentPopupClosed();
        command.Run(targets, number, view);
    }

    private void OnHelpMenuSelection(long id)
    {
        var p = GetParent<GameController>();
        p.TestFunction();
    }

    private void OnInsertMenuSelection(long id)
    {
        if (id == 1)
        {
            ShowPrototypeManifest();
        }

        if (id == 2)
        {
            ShowComponentDefinition();
        }
    }

    private void OnEditMenuSelection(long id)
    {
        if (id == 1)
        {
            ShowTemplateEditor();
        }

        if (id == 2)
        {
            ShowDatasetEditor();
        }

        if (id == 3)
        {
            ShowPrototypeManifest();
        }

        if (id == 4)
        {
            ShowImageManager();
        }

        if (id == 5)
        {
            ShowProjectSettings();
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

    public const int LongClickTime = 1000;

    public float HandY => _handManager.HandY;

    public HandManager HandManager => _handManager;

    #region UI Texture Paths

    private const string _texUiBase = "res://Textures/UI/";

    public const string TextureUI_Add = _texUiBase + "add16.png";
    public const string TextureUI_ArrowDown = _texUiBase + "arrowdown16.png";
    public const string TextureUI_ArrowUp = _texUiBase + "arrowup16.png";
    public const string TextureUI_BackSurface = _texUiBase + "back_surface16.png";
    public const string TextureUI_Bottom = _texUiBase + "bottom.png";
    public const string TextureUI_Bounds = _texUiBase + "bounds16.png";
    public const string TextureUI_Center = _texUiBase + "center.png";
    public const string TextureUI_Checkbox = _texUiBase + "checkbox16.png";
    public const string TextureUI_Circle =
        _texUiBase + "circle_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_ContentCopy =
        _texUiBase + "content_copy_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_CropLandscape =
        _texUiBase + "crop_landscape_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_CropPortrait =
        _texUiBase + "crop_portrait_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Cube128 = _texUiBase + "cube128.png";
    public const string TextureUI_Cube64 = _texUiBase + "cube64.png";
    public const string TextureUI_Cube3d =
        _texUiBase + "deployed_code_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Flip =
        _texUiBase + "flip_camera_android_16dp_F0F0F0_FILL0_wght400_GRAD0_opsz20.svg";
    public const string TextureUI_FolderOpen =
        _texUiBase + "folder_open_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_FrontSurface = _texUiBase + "front_surface16.png";
    public const string TextureUI_Fullscreen = _texUiBase + "fullscreen.png";
    public const string TextureUI_Grid16 = _texUiBase + "Grid16.png";
    public const string TextureUI_Grid24 =
        _texUiBase + "grid_on_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_HorTrack = _texUiBase + "HorTrack.png";
    public const string TextureUI_Die =
        _texUiBase + "ifl_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Image = _texUiBase + "image16.png";
    public const string TextureUI_Inventory =
        _texUiBase + "inventory_2_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Inventory_Alt =
        _texUiBase + "inventory_2_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24 (1).svg";
    public const string TextureUI_Share =
        _texUiBase + "ios_share_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Left = _texUiBase + "left.png";
    public const string TextureUI_Lock16 =
        _texUiBase + "lock_16dp_F0F0F0_FILL0_wght400_GRAD0_opsz20.svg";
    public const string TextureUI_Lock24 =
        _texUiBase + "lock_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Meeple24 = _texUiBase + "meeple24.png";
    public const string TextureUI_Meeple32 = _texUiBase + "meeple32.png";
    public const string TextureUI_MeepleOutline24 = _texUiBase + "meepleo24.png";
    public const string TextureUI_MenuWhite = _texUiBase + "menu white.png";
    public const string TextureUI_Menu = _texUiBase + "menu.png";
    public const string TextureUI_Menu16 = _texUiBase + "menu16.png";
    public const string TextureUI_Menu24 = _texUiBase + "menu24.png";
    public const string TextureUI_Menu32 = _texUiBase + "menu32.png";
    public const string TextureUI_Middle = _texUiBase + "middle.png";
    public const string TextureUI_Outbox24 =
        _texUiBase + "outbox_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg";
    public const string TextureUI_Pencil = _texUiBase + "pencil.png";
    public const string TextureUI_PerimTrack = _texUiBase + "PerimTrack.png";
    public const string TextureUI_Right = _texUiBase + "right.png";
    public const string TextureUI_RotateRight = _texUiBase + "rotate-right.png";
    public const string TextureUI_Text = _texUiBase + "text.png";
    public const string TextureUI_Top = _texUiBase + "top.png";
    public const string TextureUI_TrashCan = _texUiBase + "trash-can.png";
    public const string TextureUI_VerTrack = _texUiBase + "VerTrack.png";
    public const string TextureUI_Visibility = _texUiBase + "visibility16.png";
    public const string TextureUI_VisibilityOff = _texUiBase + "visibility_off16.png";
    public const string TextureUI_ZoomIn = _texUiBase + "zoom-in.png";
    public const string TextureUI_ZoomOut = _texUiBase + "zoom-out.png";

    #endregion
}

public class SceneModeChangeArgs : EventArgs
{
    public SceneMode NewMode { get; set; }
}

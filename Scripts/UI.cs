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
    private Button _editModeButton;

    public event EventHandler<SceneModeChangeArgs> SceneModeChange;

    private ComponentDefinition _componentDefinition;
    private TemplateCreator _templateCreator;

    private PopupMenu _editMenu;
    private PopupMenu _insertMenu;
    private PopupMenu _helpMenu;
    private PopupMenu _fileMenu;

    private PopupMenu _componentPopup;
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

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        // don't close automatically in case their are unsaved changes
        GetTree().AutoAcceptQuit = false;

        modeButtons = GetNode<HBoxContainer>("Mode");
        var buttons = modeButtons.GetChildren();
        baseFontColor = new Color(1, 1, 1, 1);

        SetSceneMode(Config.Registry.Get<SceneMode>("SceneMode"));

        _editModeButton = new Button { Text = "Edit Snapshot", ToggleMode = true };
        _editModeButton.Toggled += OnEditModeButtonToggled;
        modeButtons.AddChild(_editModeButton);

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
        _fileMenu.AboutToPopup += RebuildRestoreSnapshotMenu;

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

        _componentPopup = GetNode<PopupMenu>("ComponentPopup");
        _componentPopup.IdPressed += PopupMenuCommandSelected;
        _componentPopup.CloseRequested += ComponentPopupClosed;

        _componentName = GetNode<Label>("%ComponentName");

        _textureFactory = GetNode<TextureFactory>("%TextureFactory");

        _modalDialogs = GetNode("%ModalDialogs");

        _handManager = GetNode<HandManager>("%Hand");
        _opponentHands = GetNode<PlayerHandsPanel>("%PlayerHandsPanel");
        _opponentHands.ShowHideToggled += OnOpponentHandsShowHideToggled;

        // Defer position capture until layout is resolved.
        CallDeferred(nameof(InitOpponentHandsPositions));

        EventBus.Instance.Subscribe<ProjectChangedEvent>(ProjectChanged);
        EventBus.Instance.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        EventBus.Instance.Subscribe<EditPrototypeEvent>(ShowComponentEditDialog);
        EventBus.Instance.Subscribe<ShowTemplateEditor>(ShowTemplateEditorFromEvent);
        EventBus.Instance.Subscribe<ShowDatasetEditor>(ShowDatasetEditorFromEvent);
        EventBus.Instance.Subscribe<ShowImageManagerEvent>(ShowImageManagerFromEvent);
        EventBus.Instance.Subscribe<ShowComponentPreviewDialogEvent>(ShowComponentPreviewDialog);
        EventBus.Instance.Subscribe<ProjectSettingsChangedEvent>(OnProjectSettingsChanged);
        EventBus.Instance.Subscribe<RequestPlayerPositionEvent>(OnRequestPlayerPosition);
    }

    private void OnProjectSettingsChanged()
    {
        var s = ProjectService.Instance.CurrentProject.GameSettings;

        if (_handManager != null)
        {
            HandManager.Visible = s.EnablePlayerHands;
            _opponentHands.Visible = s.EnablePlayerHands;
        }

        if (_rotationStep != null)
            _rotationStep.Selected = s.RotationStepIndex;
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
        var settings = ProjectService.Instance.CurrentProject?.GameSettings;
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

    private void ProjectChanged(ProjectChangedEvent obj)
    {
        RebuildRestoreSnapshotMenu();
        UpdateEditModeButton(GameStatesStore.Instance?.EditMode ?? false);
    }

    private void OnGameStateChanged(GameStateChangedEvent e)
    {
        RebuildRestoreSnapshotMenu();
        UpdateEditModeButton(e.Editing);
        _gameController?.MainScene?.Table?.SetEditMode(e.Editing);
    }

    private void UpdateEditModeButton(bool editing)
    {
        if (_editModeButton == null)
            return;

        _editModeButton.SetPressedNoSignal(editing);
        _editModeButton.Text = editing ? "Editing Snapshot" : "Edit Snapshot";

        bool hasActive =
            (ProjectService.Instance?.CurrentProject?.ActiveGameState ?? SnowTag.Empty)
            != SnowTag.Empty;
        _editModeButton.Disabled = !editing && !hasActive;
    }

    private void OnEditModeButtonToggled(bool on)
    {
        // open a confirmation dialogue if there's unsaved changes
        if (on && !ProjectService.Instance.ActiveTableMatchesSnapshot())
        {
            var confirm = new ConfirmationDialog
            {
                Title = "Enter Edit Mode",
                DialogText =
                    "Editing loads the snapshot and resets the table for all players. "
                    + "Discard the current play state?",
                OkButtonText = "Edit",
            };
            confirm.Confirmed += () =>
            {
                ProjectService.Instance.SetEditMode(true);
                confirm.QueueFree();
            };
            confirm.Canceled += () =>
            {
                _editModeButton.SetPressedNoSignal(false); // revert the toggle
                confirm.QueueFree();
            };
            _modalDialogs.AddChild(confirm);
            confirm.PopupCentered();
            return;
        }

        ProjectService.Instance.SetEditMode(on);
    }

    private void RebuildRestoreSnapshotMenu()
    {
        if (_restoreSnapshotMenu == null)
            return;

        _restoreSnapshotMenu.Clear();

        var project = ProjectService.Instance.CurrentProject;

        var editing = GameStatesStore.Instance?.EditMode ?? false;
        var updateIdx = _fileMenu.GetItemIndex(6);
        if (updateIdx >= 0)
            _fileMenu.SetItemDisabled(
                updateIdx,
                editing || project == null || project.ActiveGameState == SnowTag.Empty
            );

        var ordered = OrderedGameStates(project);
        if (ordered.Count == 0)
        {
            _restoreSnapshotMenu.AddItem("(no snapshots)", -1);
            _restoreSnapshotMenu.SetItemDisabled(0, true);
            return;
        }

        foreach (var (state, _) in ordered)
            _restoreSnapshotMenu.AddItem(GameStateLabel(project, state), state.Id.Value);
    }

    /// <summary>Non-deleted snapshots in hierarchical order with depth.</summary>
    private static List<(GameState State, int Depth)> OrderedGameStates(Project project)
    {
        var result = new List<(GameState, int)>();
        if (project == null)
            return result;

        var alive = project.GameStates.Values.Where(s => !s.Deleted).ToList();
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

    private static string GameStateLabel(Project project, GameState state)
    {
        var marker = state.Id == project.ActiveGameState ? "● " : "";
        var parens =
            state.Parent != SnowTag.Empty
            && project.GameStates.TryGetValue(state.Parent, out var parent)
                ? $" ({parent.Name})"
                : "";
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
        _componentDefinition.Initialize(ProjectService.Instance.CurrentProject);
        _componentDefinition.SetTextureFactory(_textureFactory);

        _componentDefinition.CreateObject += OnCreateObject;
        _componentDefinition.CancelDialog += OnCancelCreate;

        _modalDialogs.AddChild(_componentDefinition);
    }

    private void ShowPrototypeManifest()
    {
        var s = "res://Scenes/Prototypes/PrototypeManifest.tscn";
        _prototypeManifest = GD.Load<PackedScene>(s).Instantiate<PrototypeManifest>();
        _prototypeManifest.TextureFactory = _textureFactory;
        _prototypeManifest.Refresh(_gameController.MainScene.GameObjects.PrototypeCounts());
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
        _templateCreator.SetTemplateById(e.TemplateRef);
        _modalDialogs.AddChild(_templateCreator);
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
        _datasetEditor.SetDatasetById(e.DatasetRef);

        _modalDialogs.AddChild(_datasetEditor);
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
                    ProjectService.Instance.CurrentProject?.ActiveGameState ?? SnowTag.Empty
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
        var dialog = new ConfirmationDialog
        {
            Title = "Create Snapshot",
            OkButtonText = "Create"
        };

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

        var project = ProjectService.Instance.CurrentProject;
        var parent = project?.GetGameState(project.ActiveGameState);
        CheckBox linkCheck = null;
        if (parent != null)
        {
            linkCheck = new CheckBox
            {
                Text = $"link to {parent.Name}",
                ButtonPressed = false,
                TooltipText =
                    $"When checked, this snapshot will copy updates to components in {parent.Name}.",
            };
            vbox.AddChild(linkCheck);
        }

        dialog.AddChild(vbox);

        dialog.Confirmed += () =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrEmpty(name))
                ProjectService.Instance.SaveGameState(
                    name,
                    descInput.Text.Trim(),
                    linkCheck?.ButtonPressed ?? false
                );
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();

        _modalDialogs.AddChild(dialog);
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

        void RefreshList()
        {
            list.Clear();
            foreach (var (state, _) in OrderedGameStates(project))
            {
                var idx = list.AddItem(GameStateLabel(project, state));
                list.SetItemMetadata(idx, state.Id.Value);
            }
        }

        RefreshList();

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
            RefreshList();
        };
        hbox.AddChild(deleteBtn);

        var closeBtn = new Button();
        closeBtn.Text = "Close";
        closeBtn.Pressed += () => dialog.QueueFree();
        hbox.AddChild(closeBtn);

        dialog.CloseRequested += () => dialog.QueueFree();

        _modalDialogs.AddChild(dialog);
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
        if (
            !ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(
                editEvent.PrototypeId,
                out var p
            )
        )
        {
            return;
        }
        _editingPrototypeId = editEvent.PrototypeId;

        var s = "res://Scenes/ComponentPanels/component_definition.tscn";
        _editPanel = GD.Load<PackedScene>(s).Instantiate<ComponentDefinition>();
        _editPanel.SetEditMode();
        _editPanel.SetTextureFactory(_textureFactory);
        _editPanel.DisplayPrototype(p);
        _editPanel.Initialize(ProjectService.Instance.CurrentProject);

        _editPanel.CancelDialog += CloseComponentEditDialog;
        _editPanel.CloseDialog += CloseComponentEditDialog;
        _modalDialogs.AddChild(_editPanel);
    }

    private void CloseComponentEditDialog(object sender, EventArgs e)
    {
        _editPanel.Hide();
        _editPanel.QueueFree();
        _editingPrototypeId = SnowTag.Empty;
    }

    public override void _Process(double delta)
    {
        //Below is a hack to work around the CloseRequested signal not getting fired properly

        if (_popupShown && _componentPopup.Visible)
        {
            _popupShown = false;
            ComponentPopupClosed();
        }

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

    private void AddItemToPopupMenu(
        PopupMenu popup,
        VisualCommand command,
        string caption,
        string icon,
        bool enabled = true,
        bool checkable = false,
        bool isChecked = false,
        bool addQtySubmenu = false
    )
    {
        int index = -1;
        int id = (int)command;

        if (checkable)
        {
            if (!string.IsNullOrEmpty(icon))
            {
                popup.AddCheckItem(caption, id);
            }
            else
            {
                //TODO Enable icon
                popup.AddCheckItem(caption, id);
            }

            index = popup.GetItemIndex(id);
            popup.SetItemChecked(index, isChecked);
        }
        else
        {
            if (!string.IsNullOrEmpty(icon))
            {
                popup.AddItem(caption, id);
            }
            else
            {
                //TODO Enable icon
                popup.AddItem(caption, id);
            }

            index = popup.GetItemIndex(id);
        }

        popup.SetItemDisabled(index, !enabled);

        if (addQtySubmenu)
        {
            // Submenu item IDs are encoded as: baseId * 100 + qty
            // qty 1-5 = draw that many; qty 0 = draw all
            var sub = new PopupMenu();
            sub.Name = $"QtySubmenu_{id}";

            for (int qty = 1; qty <= MAX_CARD_DEAL; qty++)
                sub.AddItem(qty.ToString(), id * 100 + qty);
            sub.AddItem("All", id * 100 + 0);

            sub.IdPressed += OnQtySubmenuItemSelected;
            popup.AddChild(sub);
            popup.SetItemSubmenu(index, sub.Name);
        }
    }

    public const int MAX_CARD_DEAL = 8;

    //we need to save which components are being affected by the right-click menu when it pops up
    private List<VisualComponentBase> _popupComponents;

    public void BuildPopupMenu(List<VisualComponentBase> components)
    {
        if (components.Count == 0)
            return; //TODO Right click menu for table surface?

        _popupComponents = components;

        bool excludeSingle = components.Count > 1;
        var comDic = new Dictionary<VisualCommand, int>();

        var fullCommands = new List<MenuCommand>();

        foreach (var c in components)
        {
            var cList = c.GetMenuCommands();
            foreach (var m in cList)
            {
                if (m.SingleOnly && excludeSingle)
                    continue; //skip commands that are single only if we have more than one comp selected

                fullCommands.Add(m);
                if (comDic.ContainsKey(m.Command))
                {
                    comDic[m.Command]++;
                }
                else
                {
                    comDic.Add(m.Command, 1);
                }
            }
        }

        //only include menu commands that are valid for all selected items
        var commands = comDic.Where(x => x.Value == components.Count).Select(y => y.Key);

        _componentPopup.Clear();

        // Build a menu from the commands that are valid for all selected items
        foreach (var command in commands)
        {
            var menuCommand = fullCommands.First(x => x.Command == command); // Get the MenuCommand object to access IsChecked and other properties

            switch (command)
            {
                case VisualCommand.Deal:
                case VisualCommand.Draw:
                    AddItemToPopupMenu(
                        _componentPopup,
                        command,
                        menuCommand.Caption,
                        string.Empty,
                        true,
                        false,
                        false,
                        addQtySubmenu: true
                    );
                    break;

                default:
                    AddItemToPopupMenu(
                        _componentPopup,
                        command,
                        menuCommand.Caption,
                        string.Empty, // Icon path, currently empty
                        true, // Enabled by default, handled by command logic
                        false
                    ); // Not checkable
                    break;
            }
        }
    }

    private void PopupMenuCommandSelected(long id)
    {
        // Submenu items handled by OnQtySubmenuItemSelected; skip raw command IDs
        // that belong to commands with qty submenus (they are parent labels, not actions).
        if (id >= (int)VisualCommand.MaximumVC)
            return;

        VisualCommand vc = (VisualCommand)id;
        if (GetParent() is GameController gc)
        {
            gc.ProcessPopupCommand(vc, _popupComponents);
        }
    }

    private void OnQtySubmenuItemSelected(long encodedId)
    {
        // Decode: baseId * 100 + qty  (qty 0 = All)
        int qty = (int)(encodedId % 100);
        VisualCommand vc = (VisualCommand)(encodedId / 100);

        if (qty == 0)
            qty = int.MaxValue; // sentinel meaning "all"

        if (GetParent() is GameController gc)
        {
            gc.ProcessPopupCommandWithQuantity(vc, _popupComponents, qty);
        }
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

    private bool _popupShown;
    private ProjectSettingsDialog _projectSettings;

    public void ShowComponentPopup(Vector2I position)
    {
        _componentPopup.Visible = true;
        _componentPopup.Position = position;
        _popupShown = true;
    }

    public void HideComponentPopup()
    {
        _componentPopup.Visible = false;
    }

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

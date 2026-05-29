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
    
    private OptionButton _rotationStep;

    private Node _modalDialogs;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        modeButtons = GetNode<HBoxContainer>("Mode");
        var buttons = modeButtons.GetChildren();
        baseFontColor = new Color(1, 1, 1, 1);

        SetSceneMode(Config.Registry.Get<SceneMode>("SceneMode"));

        _fileMenu = GetNode<PopupMenu>("%File");
        _fileMenu.AddSeparator();
        _fileMenu.AddItem("Save Snapshot...", 3);

        _restoreSnapshotMenu = new PopupMenu();
        _restoreSnapshotMenu.Name = "RestoreSnapshotMenu";
        _restoreSnapshotMenu.IdPressed += OnRestoreSnapshotSelected;
        _fileMenu.AddChild(_restoreSnapshotMenu);
        _fileMenu.AddSubmenuNodeItem("Restore Snapshot", _restoreSnapshotMenu, 4);

        _fileMenu.AddItem("Snapshot Manager...", 5);
        _fileMenu.AddSeparator();
        _fileMenu.AddItem("Multiplayer...", 10);
        _fileMenu.IdPressed += FileMenuOnIdPressed;
        _fileMenu.AboutToPopup += RebuildRestoreSnapshotMenu;

        _editMenu = GetNode<PopupMenu>("%Edit");
        _editMenu.AddItem("Templates", 1);
        _editMenu.AddItem("Datasets", 2);
        _editMenu.AddItem("Images", 4);
        _editMenu.AddItem("Prototype Manifest", 3);
        _editMenu.IdPressed += OnEditMenuSelection;

        _insertMenu = GetNode<PopupMenu>("%Insert");
        _insertMenu.AddItem("Component", 1);
        _insertMenu.AddItem("Zone", 2);
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

        EventBus.Instance.Subscribe<ProjectChangedEvent>(ProjectChanged);
        EventBus.Instance.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
        EventBus.Instance.Subscribe<EditPrototypeEvent>(ShowComponentEditDialog);
        EventBus.Instance.Subscribe<ShowTemplateEditor>(ShowTemplateEditorFromEvent);
        EventBus.Instance.Subscribe<ShowDatasetEditor>(ShowDatasetEditorFromEvent);
        EventBus.Instance.Subscribe<ShowImageManagerEvent>(ShowImageManagerFromEvent);
        EventBus.Instance.Subscribe<ShowComponentPreviewDialogEvent>(ShowComponentPreviewDialog);
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
    }

    private void OnGameStateChanged(GameStateChangedEvent e)
    {
        RebuildRestoreSnapshotMenu();
    }

    private void RebuildRestoreSnapshotMenu()
    {
        if (_restoreSnapshotMenu == null)
            return;

        _restoreSnapshotMenu.Clear();

        var project = ProjectService.Instance.CurrentProject;
        if (project == null || project.GameStates.Count == 0)
        {
            _restoreSnapshotMenu.AddItem("(no snapshots)", -1);
            _restoreSnapshotMenu.SetItemDisabled(0, true);
            return;
        }

        int id = 0;
        foreach (var name in project.GameStates.Keys.OrderBy(k => k))
        {
            _restoreSnapshotMenu.AddItem(name, id);
            id++;
        }
    }

    private void OnRestoreSnapshotSelected(long id)
    {
        var project = ProjectService.Instance.CurrentProject;
        if (project == null)
            return;

        var name = _restoreSnapshotMenu.GetItemText((int)id);
        _gameController.MainScene.GameObjects.RestoreGameState(name);
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
        ShowTemplateEditorFromEvent(new ShowTemplateEditor { TemplateName = null });
    }

    private void ShowTemplateEditorFromEvent(ShowTemplateEditor e)
    {
        string s = "res://Scenes/Templating/TemplateCreator.tscn";
        _templateCreator = GD.Load<PackedScene>(s).Instantiate<TemplateCreator>();
        _templateCreator.TextureFactory = _textureFactory;
        _templateCreator.Closed += TemplateCreatorOnClosed;
        _templateCreator.SetTemplateByName(e.TemplateName);
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
        ShowDatasetEditorFromEvent(new ShowDatasetEditor { DatasetName = null });
    }

    private void ShowDatasetEditorFromEvent(ShowDatasetEditor e)
    {
        string s = "res://Scenes/DataSet/DatasetEditor.tscn";
        _datasetEditor = GD.Load<PackedScene>(s).Instantiate<DatasetEditor>();
        _datasetEditor.Closed += DatasetEditorOnClosed;

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
                ProjectService.Instance.CurrentProject = p;
                break;

            case 2:
                ProjectService.Instance.SaveProject();
                break;

            case 3:
                ShowSaveSnapshotDialog();
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

    private void ShowSaveSnapshotDialog()
    {
        var dialog = new ConfirmationDialog();
        dialog.Title = "Save Snapshot";
        dialog.OkButtonText = "Save";

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

        dialog.AddChild(vbox);

        dialog.Confirmed += () =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                _gameController.MainScene.GameObjects.CaptureGameState(name, descInput.Text.Trim());
                EventBus.Instance.Publish(new GameStateChangedEvent());
            }
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
        dialog.Title = "Snapshot Manager";
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
            foreach (var name in project.GameStates.Keys.OrderBy(k => k))
                list.AddItem(name);
        }

        RefreshList();

        var hbox = new HBoxContainer();
        vbox.AddChild(hbox);

        var restoreBtn = new Button();
        restoreBtn.Text = "Restore";
        restoreBtn.Pressed += () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0)
                return;
            var name = list.GetItemText(sel[0]);
            _gameController.MainScene.GameObjects.RestoreGameState(name);
        };
        hbox.AddChild(restoreBtn);

        var deleteBtn = new Button();
        deleteBtn.Text = "Delete";
        deleteBtn.Pressed += () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0)
                return;
            var name = list.GetItemText(sel[0]);
            _gameController.MainScene.GameObjects.DeleteGameState(name);
            EventBus.Instance.Publish(new GameStateChangedEvent());
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
        var s = "res://Scenes/project_manager.tscn";
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
    private Guid _editingPrototypeId;

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

        _editPanel.CancelDialog += ComponentEditDialogCancel;
        _editPanel.CloseDialog += ComponentEditDialogClose;
        _modalDialogs.AddChild(_editPanel);
    }

    private void ComponentEditDialogCancel(object sender, EventArgs e)
    {
        CloseComponentEditDialog();
    }

    private void ComponentEditDialogClose(object sender, EventArgs e)
    {
        EventBus.Instance.Publish(new PrototypeChangedEvent { PrototypeId = _editingPrototypeId });
        CloseComponentEditDialog();
    }

    private void CloseComponentEditDialog()
    {
        _editPanel.Hide();
        _editPanel.QueueFree();
        _editingPrototypeId = Guid.Empty;
    }

    public override void _Process(double delta)
    {
        //Below is a hack to work around the CloseRequested signal not getting fired properly

        if (_popupShown && _componentPopup.Visible)
        {
            _popupShown = false;
            ComponentPopupClosed();
        }

        ModalDialogShown = _modalDialogs.GetChildCount() > 0;
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
        bool isChecked = false
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
    }

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
                case VisualCommand.ToggleLock:
                    AddItemToPopupMenu(
                        _componentPopup,
                        command,
                        "Frozen",
                        string.Empty, // Icon path, currently empty
                        true, // Enabled by default, handled by command logic
                        true, // Checkable
                        menuCommand.IsChecked
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
        if (id >= (int)VisualCommand.MaximumVC)
            return;

        VisualCommand vc = (VisualCommand)id;
        if (GetParent() is GameController gc)
        {
            gc.ProcessPopupCommand(vc, _popupComponents);
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

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

    public event EventHandler<MasterModeChangeArgs> MasterModeChange;

    private ComponentDefinition _componentDefinition;
    private TemplateCreator _templateCreator;

    private PopupMenu _editMenu;
    private PopupMenu _insertMenu;
    private PopupMenu _helpMenu;
    private PopupMenu _fileMenu;

    private PopupMenu _componentPopup;
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

    private Node _modalDialogs;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        modeButtons = GetNode<HBoxContainer>("Mode");
        var buttons = modeButtons.GetChildren();
        baseFontColor = new Color(1, 1, 1, 1);

        SetMasterMode(MasterMode.TwoD);

        _fileMenu = GetNode<PopupMenu>("MenuBar/File");
        _fileMenu.AddSeparator();
        _fileMenu.AddItem("Multiplayer...", 10);
        _fileMenu.IdPressed += FileMenuOnIdPressed;
        //_componentDefinition = GetNode<ComponentDefinition>("ComponentDefinition");
        //_componentDefinition.CreateObject += OnCreateObject;
        //_componentDefinition.CancelDialog += OnCancelCreate;

        _editMenu = GetNode<PopupMenu>("MenuBar/Edit");
        _editMenu.AddItem("Templates", 1);
        _editMenu.AddItem("Datasets", 2);
        _editMenu.AddItem("Images", 4);
        _editMenu.AddItem("Prototype Manifest", 3);
        _editMenu.IdPressed += OnEditMenuSelection;

        _insertMenu = GetNode<PopupMenu>("MenuBar/Insert");
        _insertMenu.AddItem("Component", 1);
        _insertMenu.AddItem("Zone", 2);
        _insertMenu.IdPressed += OnInsertMenuSelection;

        _helpMenu = GetNode<PopupMenu>("MenuBar/Help");
        _helpMenu.AddItem("Test Function", 1);
        _helpMenu.IdPressed += OnHelpMenuSelection;

        _componentPopup = GetNode<PopupMenu>("ComponentPopup");
        _componentPopup.IdPressed += PopupMenuCommandSelected;
        _componentPopup.CloseRequested += ComponentPopupClosed;

        _componentName = GetNode<Label>("%ComponentName");

        _textureFactory = GetNode<TextureFactory>("%TextureFactory");

        _modalDialogs = GetNode("%ModalDialogs");

        EventBus.Instance.Subscribe<ProjectChangedEvent>(ProjectChanged);
        EventBus.Instance.Subscribe<EditPrototypeEvent>(ShowComponentEditDialog);
        EventBus.Instance.Subscribe<ShowTemplateEditor>(ShowTemplateEditorFromEvent);
        EventBus.Instance.Subscribe<ShowDatasetEditor>(ShowDatasetEditorFromEvent);
        EventBus.Instance.Subscribe<ShowImageManagerEvent>(ShowImageManagerFromEvent);
    }


    private void ProjectChanged(ProjectChangedEvent obj)
    {
        return;
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
                var op = _projectManager.CreateTestProject();
                ProjectService.Instance.SaveProject(op);
                break;

            case 10:
                ShowMultiplayerDialog();
                break;
        }
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
            if (value == _modalDialogShown) return;
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

        if (commands.Any(x => x == VisualCommand.ToggleLock))
        {
            var isChecked = fullCommands
                .Where(x => x.Command == VisualCommand.ToggleLock)
                .All(y => y.IsChecked);
            AddItemToPopupMenu(
                _componentPopup,
                VisualCommand.ToggleLock,
                "Frozen",
                string.Empty,
                true,
                true,
                isChecked
            );
        }

        if (commands.Any(x => x == VisualCommand.Roll))
            AddItemToPopupMenu(_componentPopup, VisualCommand.Roll, "Roll", string.Empty);

        if (commands.Any(x => x == VisualCommand.Flip))
            AddItemToPopupMenu(_componentPopup, VisualCommand.Flip, "Flip", string.Empty);

        if (commands.Any(x => x == VisualCommand.Delete))
            AddItemToPopupMenu(_componentPopup, VisualCommand.Delete, "Delete", string.Empty);

        if (commands.Any(x => x == VisualCommand.Shuffle))
            AddItemToPopupMenu(_componentPopup, VisualCommand.Shuffle, "Shuffle", string.Empty);

        if (commands.Any(x => x == VisualCommand.Edit))
            AddItemToPopupMenu(_componentPopup, VisualCommand.Edit, "Edit", string.Empty);

        if (commands.Any(x => x == VisualCommand.MakeUnique))
            AddItemToPopupMenu(
                _componentPopup,
                VisualCommand.MakeUnique,
                "Make Unique",
                string.Empty
            );
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

    public enum MasterMode
    {
        TwoD,
        ThreeD,
        Designer,
    };

    public MasterMode CurMasterMode { get; set; }

    private void SetMasterMode(MasterMode mode)
    {
        GD.Print($"Set Master Mode {mode}");
        var buttons = modeButtons.GetChildren();
        foreach (var i in buttons)
        {
            if (i is Button b)
            {
                b.RemoveThemeColorOverride("font_color");
                b.RemoveThemeColorOverride("font_focus_color");
            }
        }

        var buttonNum = 0;

        switch (mode)
        {
            case MasterMode.TwoD:
                buttonNum = 0;
                break;
            case MasterMode.ThreeD:
                buttonNum = 1;
                break;
            case MasterMode.Designer:
                buttonNum = 2;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        if (buttons[buttonNum] is Button target)
        {
            target.AddThemeColorOverride("font_color", highlightFontColor);
            target.AddThemeColorOverride("font_focus_color", highlightFontColor);
        }

        CurMasterMode = mode;
        MasterModeChange?.Invoke(this, new MasterModeChangeArgs { NewMode = mode });
    }

    private void _on_play_2d_pressed()
    {
        SetMasterMode(MasterMode.TwoD);
    }

    private void _on_play_3d_pressed()
    {
        SetMasterMode(MasterMode.ThreeD);
    }

    private void _on_designer_pressed()
    {
        SetMasterMode(MasterMode.Designer);
        TextureTest();
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
}

public class MasterModeChangeArgs : EventArgs
{
    public UI.MasterMode NewMode { get; set; }
}

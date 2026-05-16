using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class ComponentDefinition : Window
{
    // Called when the node enters the scene tree for the first time.
    [Export]
    private ComponentTemplate[] _components;

    private VBoxContainer buttonPanel;

    private Panel _componentPanel;

    private string _buttonTemplate = "res://Scenes/ComponentPanels/component_type_button.tscn"; //button to copy for 'sidepane' buttons.

    private Dictionary<string, CanvasItem> _panelDictionary = new();

    private Button _createButton;

    private Button _cancelButton;
    private Button _updateButton;

    private TextureFactory _textureFactory;

    public Project CurrentProject { get; set; }

    private AcceptDialog _errorDialog;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _errorDialog = new AcceptDialog();
        _errorDialog.Title = "Validation Error";
        AddChild(_errorDialog);

        _createButton = GetNode<Button>("%CreateButton");
        _createButton.Pressed += CreateClicked;

        _cancelButton = GetNode<Button>("%CancelButton");
        _cancelButton.Pressed += CancelClicked;

        _updateButton = GetNode<Button>("%UpdateButton");
        _updateButton.Pressed += UpdateClicked;

        if (_initRequired)
        {
            LocalInit();
        }
        if (_editMode)
            SetEditMode();
    }

    private bool _initRequired;

    private bool _isInitialized;

    public void Initialize(Project curProject)
    {
        CurrentProject = curProject;
        if (IsNodeReady())
        {
            LocalInit();
        }
        else
        {
            _initRequired = true;
        }
    }

    private void LocalInit()
    {
        _initRequired = false;

        Visible = true;

        if (_isInitialized)
            return;

        _isInitialized = true;

        buttonPanel = GetNode<VBoxContainer>("%CompButtonStrip");
        _componentPanel = GetNode<Panel>("%ComponentPanel");

        var bg = new ButtonGroup();

        bool firstButton = true;

        foreach (var c in _components)
        {
            var b = CreateButton(c.ComponentName, c.Icon, bg);
            buttonPanel.AddChild(b);

            var ci = CreateComponentPanel(c.DefinitionDialogName);

            if (ci is ComponentPanelDialogResult cpdr)
            {
                cpdr.TextureFactory = _textureFactory;
                cpdr.CurrentProject = CurrentProject;
            }

            _componentPanel.AddChild(ci);

            _panelDictionary.Add(c.ComponentName, ci);

            if (firstButton && !_editMode)
            {
                b.ButtonPressed = true;
                CurName = c.ComponentName;
                firstButton = false;
            }

            if (_editMode)
            {
                UpdatePanelVisibility(CurName);
            }
        }

        if (_editMode && _mapPrototype != null)
        {
            MapPrototypeToPanel(_mapPrototype);
        }
    }

    private string _curItem;

    private void CreateClicked()
    {
        if (_panelDictionary[CurName] is ComponentPanelDialogResult r)
        {
            var errors = r.ValidateParameters(r.GetParams());
            if (errors != null && errors.Count > 0)
            {
                _errorDialog.DialogText = string.Join("\n", errors);
                _errorDialog.PopupCentered();
                return;
            }

            CreateObjectEventArgs e = new()
            {
                ComponentType = r.ComponentType,
                Params = r.GetParams(),
                PrototypeRef = Guid.NewGuid(),
                DataSet = r.DataSet,
                MultipleCreateMode = r.MultipleCreateMode,
                WidthHint = r.WidthHint,
                HeightHint = r.HeightHint,
            };

            if (!e.Params.ContainsKey("BaseName"))
            {
                e.Params.Add("BaseName", CurName);
            }

            var cd = _components.First(x => x.ComponentName == CurName);

            if (
                cd.PrototypeNames != null
                && cd.PrototypeNames.Length > 0
                && r.PrototypeIndex < cd.PrototypeNames.Length
            )
            {
                e.PrototypeName = cd.PrototypeNames[r.PrototypeIndex];
            }
            else
            {
                e.PrototypeName = cd.PrototypeName;
            }

            CreateObject?.Invoke(this, e);
        }
        else
        {
            GD.PrintErr($"{_panelDictionary[CurName].Name} is NOT CPDR");
        }
    }

    private void UpdateClicked()
    {
        if (_mapPrototype == null)
            return;

        if (_panelDictionary[CurName] is ComponentPanelDialogResult r)
        {
            var errors = r.ValidateParameters(r.GetParams());
            if (errors != null && errors.Count > 0)
            {
                _errorDialog.DialogText = string.Join("\n", errors);
                _errorDialog.PopupCentered();
                return;
            }
        }

        //update the project prototype

        if (
            !ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(
                _mapPrototype.PrototypeRef,
                out var prototype
            )
        )
            return;

        prototype.Parameters = (
            _panelDictionary[CurName] as ComponentPanelDialogResult
        )?.GetParams();
        prototype.Name = Utility.GetParam<string>(prototype.Parameters, "ComponentName");
        prototype.IsDirty = true;

        EventBus.Instance.Publish(
            new PrototypeChangedEvent { PrototypeId = prototype.PrototypeRef }
        );

        CloseDialog?.Invoke(this, EventArgs.Empty);
    }

    private void CancelClicked()
    {
        CancelDialog?.Invoke(this, EventArgs.Empty);
    }

    public string CurName
    {
        get => _curItem;
        set
        {
            _curItem = value;
            UpdatePanelVisibility(_curItem);
        }
    }

    private void UpdatePanelVisibility(string name)
    {
        foreach (var kv in _panelDictionary)
        {
            if (kv.Value is ComponentPanelDialogResult cpdr)
            {
                if (kv.Key == name)
                {
                    kv.Value.Visible = true;
                    if (!_editMode)
                        cpdr.Activate();
                }
                else
                {
                    kv.Value.Visible = false;
                    cpdr.Deactivate();
                }
            }
        }
    }

    public void SetTextureFactory(TextureFactory tf)
    {
        _textureFactory = tf;
        foreach (var kv in _panelDictionary)
        {
            if (kv.Value is ComponentPanelDialogResult cpdr)
            {
                cpdr.TextureFactory = tf;
            }
        }
    }

    private Button CreateButton(string name, Texture2D icon, ButtonGroup bg)
    {
        var scene = ResourceLoader.Load<PackedScene>(_buttonTemplate).Instantiate();

        if (scene is Button b)
        {
            b.Text = name;
            b.Icon = icon;
            b.ButtonGroup = bg;

            b.Pressed += () => ButtonPressed(name);
            return b;
        }

        return new Button();
    }

    public void ButtonPressed(string name)
    {
        CurName = name;
    }

    private CanvasItem CreateComponentPanel(string _panelTemplate)
    {
        var scene = ResourceLoader.Load<PackedScene>(_panelTemplate).Instantiate();

        if (scene is CanvasItem ci)
        {
            ci.Visible = false; //all panels start out hidden
            return ci;
        }

        GD.Print("Not Canvas Item");

        return null;
    }

    public event EventHandler<CreateObjectEventArgs> CreateObject;
    public event EventHandler<EventArgs> CancelDialog;
    public event EventHandler<EventArgs> CloseDialog;

    public VisualComponentBase.VisualComponentType NameToType(string name)
    {
        return Enum.Parse<VisualComponentBase.VisualComponentType>(
            _components.First(x => x.ComponentName == name).ComponentType
        );
    }

    public string TypeToName(VisualComponentBase.VisualComponentType componentType)
    {
        if (componentType == VisualComponentBase.VisualComponentType.Deck)
            return "Printed";
        return _components.First(x => x.ComponentType == componentType.ToString()).ComponentName;
    }

    public void SetCurrentComponentType(VisualComponentBase.VisualComponentType type)
    {
        CurName = TypeToName(type);
    }

    private Prototype _mapPrototype;

    public void DisplayPrototype(Prototype prototype)
    {
        if (prototype == null)
            return;

        _mapPrototype = prototype;
        var t = TypeToName(prototype.Type);
        CurName = t;

        if (IsNodeReady())
        {
            MapPrototypeToPanel(prototype);
        }
    }

    private void MapPrototypeToPanel(Prototype prototype)
    {
        foreach (var kv in _panelDictionary)
        {
            if (kv.Key == CurName && kv.Value is ComponentPanelDialogResult cpdr)
            {
                cpdr.DisplayPrototype(prototype);
                return;
            }
        }
    }

    private bool _editMode;

    public void SetEditMode()
    {
        _editMode = true;
        if (IsNodeReady())
        {
            _updateButton.Visible = true;
            _createButton.Visible = false;
            buttonPanel.Visible = false;
        }
    }
}

public class CreateObjectEventArgs : EventArgs
{
    public Dictionary<string, object> Params { get; set; }
    public VisualComponentBase.VisualComponentType ComponentType { get; set; }

    public string PrototypeName { get; set; }

    public Guid PrototypeRef { get; set; }

    public DataSet DataSet { get; set; }

    public bool MultipleCreateMode { get; set; }
    public float WidthHint { get; set; }
    public float HeightHint { get; set; }
}

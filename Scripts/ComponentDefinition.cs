using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

[View("component_definition")]
public partial class ComponentDefinition : Window, ICommandView
{
    public ViewPlace? Place(IAtomReader R) =>
        ViewPlace.Of(this, _mapPrototype?.Id ?? SnowTag.Empty);

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
        _cancelButton.Pressed += QueueFree;
        CloseRequested += QueueFree;

        _updateButton = GetNode<Button>("%UpdateButton");
        _updateButton.Pressed += UpdateClicked;

        buttonPanel = GetNode<VBoxContainer>("%CompButtonStrip");
        _componentPanel = GetNode<Panel>("%ComponentPanel");
    }

    public void Initialize(Project curProject)
    {
        CurrentProject = curProject;
        Visible = true;

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
                cpdr.SubmittableChanged += ShowSubmittable;
            }

            _componentPanel.AddChild(ci);

            _panelDictionary.Add(c.ComponentName, ci);

            if (firstButton && !_editMode)
            {
                b.ButtonPressed = true;
                CurName = c.ComponentName;
                firstButton = false;
            }
        }
    }

    private string _curItem;

    private void CreateClicked()
    {
        if (_panelDictionary[CurName] is ComponentPanelDialogResult r)
        {
            var result = r.GetParams();
            if (ShowErrors(r, result))
                return;

            var prototype = new Prototype
            {
                Id = SnowTag.Create(),
                Parameters = result,
                Name = r.PrototypeName,
            };
            Atoms.R.Write(this, prototype);

            EventBus.Instance.Publish(
                new SpawnPrototypeEvent { PrototypeRef = prototype.Id, AllRows = true }
            );
            QueueFree();
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

        if (_panelDictionary[CurName] is not ComponentPanelDialogResult r)
            return;

        var newParams = r.GetParams();
        if (ShowErrors(r, newParams))
            return;

        //update the project prototype

        var prototype = Atoms.R.Get(Prototype.ById, _mapPrototype.Id);
        if (prototype == null)
            return;

        prototype = prototype with { Parameters = newParams, Name = r.PrototypeName };

        Atoms.R.Write(
            this,
            [
                prototype,
                .. newParams is PrintedParameters printed
                    ? DeckReconciliation.Changes(Atoms.R, prototype.Id, printed)
                    : [],
            ]
        );

        QueueFree();
    }

    /// <summary>
    /// Shows what's wrong with the panel's name and <paramref name="parameters"/>, if anything.
    /// </summary>
    /// <returns>True if there were errors to show.</returns>
    private bool ShowErrors(ComponentPanelDialogResult panel, ComponentParameters parameters)
    {
        var errors = panel.ValidateParameters(parameters);
        if (string.IsNullOrEmpty(panel.PrototypeName))
            errors.Insert(0, "Name may not be blank");
        if (errors.Count == 0)
            return false;

        _errorDialog.DialogText = string.Join("\n", errors);
        _errorDialog.PopupCentered();
        return true;
    }

    private string CurName
    {
        get => _curItem;
        set
        {
            _curItem = value;
            UpdatePanelVisibility(_curItem);
        }
    }

    /// <summary>
    /// Enables Create and Update when the shown panel's parameters can be submitted.
    /// </summary>
    private void ShowSubmittable()
    {
        bool submittable =
            _panelDictionary.GetValueOrDefault(CurName) is not ComponentPanelDialogResult panel
            || panel.Submittable;
        _createButton.Disabled = !submittable;
        _updateButton.Disabled = !submittable;
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
        ShowSubmittable();
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

    private Prototype _mapPrototype;

    public void DisplayPrototype(Prototype prototype)
    {
        if (prototype == null)
            return;

        _mapPrototype = prototype;
        CurName = _components
            .First(x => x.ComponentType == prototype.Type.ToString())
            .ComponentName;
        MapPrototypeToPanel(prototype);
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
        Title = "Edit Component";
        _updateButton.Visible = true;
        _createButton.Visible = false;
        buttonPanel.Visible = false;
    }
}

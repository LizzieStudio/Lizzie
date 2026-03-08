using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

public partial class PrototypeManifest : Control
{
    private SplitContainer _mainContainer;
    private Tree _prototypeTree;
    private TreeItem _root;

    private Button _close;
 
    private ComponentPreview _preview;

    private Prototype _selectedPrototype;
    public Prototype SelectedPrototype
    {
        get => _selectedPrototype;
        private set
        {
            _selectedPrototype = value;
            OnPrototypeSelected();
        }
    }

    private int _sortColumn = 0;
    private bool _sortAscending = true;

    public override void _Ready()
    {
        _mainContainer = GetNode<SplitContainer>("%MainContainer");
        _preview = GetNode<ComponentPreview>("%ComponentPreview");
        _preview.SetComponentX(-200);

        _close  = GetNode<Button>("%Close");
        _close.Pressed += OnClose;
        
        InitializePrototypeGrid();

        if (_refreshRequired)
        {
            Refresh(_prototypeCounts);
        }
    }


    public event EventHandler Closed;
    private void OnClose()
    {
        Hide();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private ComponentDefinition _editPanel;


    private void ComponentEditDialogClose(object sender, EventArgs e)
    {
        _editPanel.Hide();
        _editPanel.QueueFree();
        RefreshSelectedPrototype();
    }

    private void RefreshSelectedPrototype()
    {
        if (SelectedPrototype == null) return;


        if (!ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(SelectedPrototype.PrototypeRef, out var p))
            return;

        SelectedPrototype = p;

        var selectedItem = _prototypeTree.GetSelected();
        if (selectedItem == null)
            return;

        var prototypeRef = Guid.Parse(selectedItem.GetMetadata(0).AsString());
        if (prototypeRef == SelectedPrototype.PrototypeRef)
        {
            selectedItem.SetText(0, p.Name);
        }
    }



    private void InitializePrototypeGrid()
    {
        _prototypeTree = new Tree();
        _prototypeTree.Columns = 3;
        _prototypeTree.HideRoot = true;
        _prototypeTree.SelectMode = Tree.SelectModeEnum.Row;
        _prototypeTree.SizeFlagsVertical = SizeFlags.ExpandFill;
        _prototypeTree.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _prototypeTree.ButtonClicked += PrototypeEditClicked;

        _prototypeTree.ColumnTitlesVisible = true;
        _prototypeTree.SetColumnTitle(0, "Name");
        _prototypeTree.SetColumnTitleAlignment(0, HorizontalAlignment.Left);
        _prototypeTree.SetColumnTitle(1, "Type");
        _prototypeTree.SetColumnTitleAlignment(1, HorizontalAlignment.Left);
        _prototypeTree.SetColumnTitle(2, "Qty");
        _prototypeTree.SetColumnTitleAlignment(2, HorizontalAlignment.Center);
        _prototypeTree.SetColumnExpand(0, true);
        _prototypeTree.SetColumnExpand(1, true);
        _prototypeTree.SetColumnExpandRatio(0, 2);
        _prototypeTree.SetColumnExpandRatio(1, 1);

        _prototypeTree.ColumnTitleClicked += OnColumnHeaderClicked;
        _prototypeTree.ItemSelected += OnTreeItemSelected;

        if (_mainContainer != null)
        {
            _mainContainer.AddChild(_prototypeTree);
            _mainContainer.MoveChild(_prototypeTree, 0);
        }
        else
        {
            AddChild(_prototypeTree);
            MoveChild(_prototypeTree, 0);
        }

        _root = _prototypeTree.CreateItem();
    }

    private void PrototypeEditClicked(TreeItem item, long column, long id, long mouseButtonIndex)
    {

        var prototypeRef = Guid.Parse(item.GetMetadata(0).AsString());

        if (!ProjectService.Instance.CurrentProject.Prototypes.TryGetValue(prototypeRef, out var p))
        {
            return;
        }

        var s = "res://Scenes/ComponentPanels/component_definition.tscn";
        _editPanel = GD.Load<PackedScene>(s).Instantiate<ComponentDefinition>();
        _editPanel.SetEditMode();
        _editPanel.SetTextureFactory(TextureFactory);
        _editPanel.DisplayPrototype(p);
        _editPanel.Initialize(ProjectService.Instance.CurrentProject);

        _editPanel.CancelDialog += ComponentEditDialogClose;
        AddChild(_editPanel);
    }


    private bool _refreshRequired;

    public void Refresh(Dictionary<Guid, int> prototypeCounts)
    {
        _prototypeCounts = prototypeCounts;

        if (!IsNodeReady())
        {
            _refreshRequired = true;
            return;
        }
        _preview.ClearComponent();
        _preview.SetComponentVisibility(false);
        LoadPrototypes(prototypeCounts);
        _refreshRequired = false;
    }

    private Dictionary<Guid, int> _prototypeCounts;

    private void LoadPrototypes(Dictionary<Guid, int> prototypeCounts)
    {
        if (ProjectService.Instance?.CurrentProject == null)
            return;

        Texture2D pencil = ResourceLoader.Load<Texture2D>("res://Textures/UI/pencil.png");

        _prototypeTree.Clear();
        _root = _prototypeTree.CreateItem();

        var prototypes = ProjectService.Instance.CurrentProject.Prototypes.Values.ToList();

        if (_sortColumn == 0)
        {
            prototypes = _sortAscending
                ? prototypes.OrderBy(p => p.Name).ToList()
                : prototypes.OrderByDescending(p => p.Name).ToList();
        }
        else if (_sortColumn == 1)
        {
            prototypes = _sortAscending
                ? prototypes.OrderBy(p => p.Type.ToString()).ToList()
                : prototypes.OrderByDescending(p => p.Type.ToString()).ToList();
        }

        foreach (var prototype in prototypes)
        {
            var item = _prototypeTree.CreateItem(_root);
            item.SetText(0, prototype.Name ?? "");
            item.SetText(1, prototype.Type.ToString());

            if (prototypeCounts != null && prototypeCounts.TryGetValue(prototype.PrototypeRef, out var count))
            {
                item.SetText(2, count.ToString());
            }
            else
            {
                item.SetText(2, "0");
            }
            item.SetTextAlignment(2, HorizontalAlignment.Center);

            item.SetMetadata(0, prototype.PrototypeRef.ToString());
            item.AddButton(2, pencil);
        }
    }

    private void OnColumnHeaderClicked(long column, long mouseButtonIndex)
    {
        if (mouseButtonIndex != (long)MouseButton.Left)
            return;

        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = (int)column;
            _sortAscending = true;
        }

        LoadPrototypes(_prototypeCounts);
    }

    private void OnTreeItemSelected()
    {
        var selectedItem = _prototypeTree.GetSelected();
        if (selectedItem == null)
            return;

        var prototypeRef = Guid.Parse(selectedItem.GetMetadata(0).AsString());

        if (ProjectService.Instance?.CurrentProject?.Prototypes.TryGetValue(prototypeRef, out var prototype) == true)
        {
            SelectedPrototype = prototype;
        }
    }

    private void OnPrototypeSelected()
    {
        //update the preview
        _preview.ClearComponent();
        _preview.Build(SelectedPrototype, TextureFactory);
    }

    public TextureFactory TextureFactory { get; set; }

}

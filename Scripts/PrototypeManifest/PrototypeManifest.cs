using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class PrototypeManifest : Window, ICommandView
{
    private SplitContainer _mainContainer;
    private Tree _prototypeTree;
    private TreeItem _root;

    private ComponentPreview _preview;

    private Button _editProto;
    private Button _cloneProto;
    private Button _spawnProto;
    private Button _deleteProto;

    private Prototype _selectedPrototype;
    private Prototype SelectedPrototype
    {
        get => _selectedPrototype;
        set
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

        CloseRequested += QueueFree;

        _editProto = GetNode<Button>("%EditProto");
        _editProto.Pressed += () =>
        {
            if (SelectedPrototype != null)
            {
                EventBus.Instance.Publish(
                    new EditPrototypeEvent { PrototypeRef = SelectedPrototype.Id }
                );
            }
        };

        _cloneProto = GetNode<Button>("%CloneProto");
        _cloneProto.Pressed += DuplicatePrototype;

        _spawnProto = GetNode<Button>("%SpawnProto");
        _spawnProto.Pressed += SpawnPrototype;

        _deleteProto = GetNode<Button>("%DeleteProto");
        _deleteProto.Pressed += DeletePrototype;

        EventBus.Instance.Subscribe<SpawnPrototypeEvent>(OnSpawn);

        InitializePrototypeGrid();
    }

    // Gets out of the way of placing a component, whether it's added from here or just created.
    private void OnSpawn(SpawnPrototypeEvent _) => QueueFree();

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    private void InitializePrototypeGrid()
    {
        _prototypeTree = new Tree();
        _prototypeTree.Columns = 3;
        _prototypeTree.HideRoot = true;
        _prototypeTree.SelectMode = Tree.SelectModeEnum.Row;
        _prototypeTree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _prototypeTree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

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

    private void SpawnPrototype()
    {
        if (_selectedPrototype != null)
        {
            EventBus.Instance.Publish(
                new SpawnPrototypeEvent { PrototypeRef = _selectedPrototype.Id }
            );
        }
    }

    private void DeletePrototype()
    {
        if (_selectedPrototype == null)
            return;

        var dialog = new ConfirmationDialog
        {
            Title = "Delete Component",
            DialogText = $"Delete \"{_selectedPrototype.Name}\"?",
            OkButtonText = "Delete",
        };

        dialog.Confirmed += () =>
        {
            Atoms.R.Write(this, _selectedPrototype with { Deleted = true });
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void DuplicatePrototype()
    {
        if (_selectedPrototype == null)
            return;

        var existingNames = Atoms.R.Get(Prototype.Live).Select(p => p.Name).ToHashSet();

        // Strip any existing trailing " (N)" suffix before generating the new name
        var baseName = System.Text.RegularExpressions.Regex.Replace(
            _selectedPrototype.Name,
            @"\s*\(\d+\)$",
            string.Empty
        );

        int n = 1;
        string newName;
        do
        {
            newName = $"{baseName} ({n})";
            n++;
        } while (existingNames.Contains(newName));

        var duplicate = new Prototype
        {
            Id = Ref<Prototype>.Create(),
            Name = newName,
            Parameters = _selectedPrototype.Parameters,
        };

        Atoms.R.Write(this, duplicate);
    }

    private void Sync(IWatchReader R)
    {
        _prototypeTree.Clear();
        _root = _prototypeTree.CreateItem();

        var prototypes = R.Get(Prototype.Live);

        // The Qty column, counted on the stage the view shows, leaving out the boxed components.
        // A prototype that spawns decks counts its decks, not their cards.
        var counts = R.Get(
                ComponentPosition.ByStage,
                ProjectService.Instance.GameObjects.ShownStage(R)
            )
            .Where(s => s.Container is not InBox)
            .Select(R.GetComponent)
            .Where(c =>
                !(
                    c.IsItem
                    && R.Get(Prototype.ById, c.PrototypeRef)?.Parameters
                        is PrintedParameters { SpawnDeck: true }
                )
            )
            .GroupBy(c => c.PrototypeRef)
            .ToDictionary(g => g.Key, g => g.Count());

        var selectedRef = _selectedPrototype?.Id ?? Ref<Prototype>.Empty;
        _selectedPrototype = prototypes.FirstOrDefault(p => p.Id == selectedRef);
        if (_selectedPrototype == null)
        {
            _preview.ClearComponent();
            _preview.SetComponentVisibility(false);
        }

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

            item.SetText(2, counts.GetValueOrDefault(prototype.Id).ToString());
            item.SetTextAlignment(2, HorizontalAlignment.Center);

            item.SetMetadata(0, prototype.Id);

            if (prototype == _selectedPrototype)
                item.Select(0);
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

        Atoms.R.QueueSync(this);
    }

    private void OnTreeItemSelected()
    {
        var selectedItem = _prototypeTree.GetSelected();
        if (selectedItem == null)
            return;

        var prototypeRef = (Ref<Prototype>)selectedItem.GetMetadata(0);

        if (prototypeRef == _selectedPrototype?.Id)
            return;

        var prototype = Atoms.R.Get(Prototype.ById, prototypeRef);
        if (prototype != null)
            SelectedPrototype = prototype;
    }

    private void OnPrototypeSelected()
    {
        //update the preview
        _preview.ClearComponent();

        _preview.Build(SelectedPrototype, TextureFactory);
    }

    public TextureFactory TextureFactory { get; set; }
}

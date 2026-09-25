using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class PrototypeManifest : Window
{
    private SplitContainer _mainContainer;
    private Tree _prototypeTree;
    private TreeItem _root;

    private Button _close;

    private ComponentPreview _preview;

    private Button _editProto;
    private Button _cloneProto;
    private Button _spawnProto;
    private Button _deleteProto;

    private Button _deleteAllUnused;
    private Button _hideUnused;

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

        _close = GetNode<Button>("%Close");
        _close.Pressed += OnClose;

        _editProto = GetNode<Button>("%EditProto");
        _editProto.Pressed += () =>
        {
            if (SelectedPrototype != null)
            {
                EventBus.Instance.Publish(
                    new EditPrototypeEvent { PrototypeId = SelectedPrototype.Id }
                );
            }
        };

        _cloneProto = GetNode<Button>("%CloneProto");
        _cloneProto.Pressed += DuplicatePrototype;

        _spawnProto = GetNode<Button>("%SpawnProto");
        _spawnProto.Pressed += SpawnPrototype;

        _deleteProto = GetNode<Button>("%DeleteProto");
        _deleteProto.Pressed += DeletePrototype;

        _deleteAllUnused = GetNode<Button>("%DeleteUnused");
        _hideUnused = GetNode<Button>("%HideUnused");

        InitializePrototypeGrid();
    }

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    public event EventHandler Closed;

    private void OnClose()
    {
        Hide();
        Closed?.Invoke(this, EventArgs.Empty);
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
            OnClose();
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
            Title = "Delete Prototype",
            DialogText = $"Delete \"{_selectedPrototype.Name}\"? This cannot be undone.",
            OkButtonText = "Delete",
        };

        dialog.Confirmed += () =>
        {
            ProjectService.Instance.Upsert(_selectedPrototype with { Deleted = true });
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

        var existingNames = ProjectService
            .Instance.Get<Prototype>()
            .Select(p => p.Name)
            .ToHashSet();

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
            Id = Snowport.Clock.CreateTag(),
            Name = newName,
            Parameters = _selectedPrototype.Parameters with { ComponentName = newName },
        };

        ProjectService.Instance.Upsert(duplicate);
    }

    private void Sync(IRecordReader R)
    {
        _prototypeTree.Clear();
        _root = _prototypeTree.CreateItem();

        var prototypes = R.Get<Prototype>().ToList();

        // The Qty column. A deck's cards carry the deck's prototype and a data row, so only
        // components without a row are counted, which counts each deck once.
        var counts = R.Get<ComponentState>(s =>
                s.DataSetRowIndex < 0 && s.DataSetRowId == SnowTag.Empty
            )
            .GroupBy(s => s.PrototypeRef)
            .ToDictionary(g => g.Key, g => g.Count());

        var selectedRef = _selectedPrototype?.Id ?? SnowTag.Empty;
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

            item.SetMetadata(0, prototype.Id.Value);

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

        ProjectService.Instance.QueueSync(this);
    }

    private void OnTreeItemSelected()
    {
        var selectedItem = _prototypeTree.GetSelected();
        if (selectedItem == null)
            return;

        var prototypeRef = new SnowTag(selectedItem.GetMetadata(0).AsInt32());

        if (prototypeRef == _selectedPrototype?.Id)
            return;

        var prototype = ProjectService.Instance.Get<Prototype>(prototypeRef);
        if (prototype != null)
            SelectedPrototype = prototype;
    }

    private void OnPrototypeSelected()
    {
        //update the preview
        _preview.ClearComponent();

        int rowIndex = 0;
        SnowTag rowId = SnowTag.Empty;

        SnowTag datasetParam = SnowTag.Empty;
        if (SelectedPrototype.Parameters is PrintedParameters pp)
        {
            datasetParam = pp.Dataset;
        }
        else if (SelectedPrototype.Parameters is DieParameters dp)
        {
            datasetParam = dp.Dataset;
        }

        if (datasetParam != SnowTag.Empty)
        {
            var rows = ProjectService.Instance.GetRows(datasetParam);
            if (rows.Count > 0)
            {
                rowIndex = -1;
                rowId = rows[0].Id;
            }
        }

        _preview.Build(SelectedPrototype, rowIndex, rowId, TextureFactory);
    }

    public TextureFactory TextureFactory { get; set; }
}

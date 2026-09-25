using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Lizzie.AssetManagement;

public partial class ImageManager : Window
{
    private Button _addNewButton;
    private Button _removeButton;
    private Button _closeButton;

    private Panel _addPanel;
    private Button _addImageButton;
    private Button _cancelImageButton;

    private OptionButton _cloudServiceOption;
    public LineEdit _urlInput;
    public LineEdit _nameInput;

    private HFlowContainer _tileContainer;

    private readonly Dictionary<SnowTag, ImageTile> _tiles = new();

    private SnowTag _selected = SnowTag.Empty;

    private const string _tileScenePath = "res://Scenes/Controls/image_tile.tscn";

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _addNewButton = GetNode<Button>("%AddNewButton");
        _removeButton = GetNode<Button>("%RemoveButton");
        _closeButton = GetNode<Button>("%CloseButton");
        _addNewButton.Pressed += OnAddNewPressed;
        _removeButton.Pressed += OnRemovePressed;
        _closeButton.Pressed += OnClosePressed;

        _addPanel = GetNode<Panel>("%AddImagePanel");
        _addImageButton = GetNode<Button>("%AddImageButton");
        _addImageButton.Pressed += OnAddImagePressed;
        _cancelImageButton = GetNode<Button>("%CancelImageButton");
        _cancelImageButton.Pressed += OnCancelImagePressed;

        _tileContainer = GetNode<HFlowContainer>("%TileContainer");

        _cloudServiceOption = GetNode<OptionButton>("%CloudService");
        _urlInput = GetNode<LineEdit>("%UrlInput");
        _nameInput = GetNode<LineEdit>("%NameInput");
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    public event EventHandler Closed;

    private void OnClosePressed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemovePressed()
    {
        var selected = ProjectService.Instance.Get<Asset>(_selected);
        if (selected != null)
            ProjectService.Instance.Upsert(selected with { Deleted = true });
    }

    private void OnTileClicked(SnowTag target)
    {
        if (_tiles.TryGetValue(_selected, out var previous))
        {
            previous.SetSelected(false);
        }

        _selected = target;

        if (_tiles.TryGetValue(_selected, out var tile))
        {
            tile.SetSelected(true);
        }
    }

    private void UpdateButtons(bool enable)
    {
        _addNewButton.Disabled = !enable;
        _removeButton.Disabled = !enable;
    }

    private void OnAddNewPressed()
    {
        _addPanel.Visible = true;
        UpdateButtons(false);
    }

    private void OnAddImagePressed()
    {
        _addPanel.Visible = false;
        UpdateButtons(true);

        //create the new asset
        var asset = new Asset
        {
            Name = _nameInput.Text,
            Type = Asset.AssetType.Image,
            CloudPath = _urlInput.Text,
        };

        ProjectService.Instance.Upsert(asset);

        _nameInput.Text = string.Empty;
        _urlInput.Text = string.Empty;
    }

    private void OnCancelImagePressed()
    {
        _addPanel.Visible = false;
        UpdateButtons(true);
    }

    /// <summary>Keeps one tile per image. Each tile shows its own asset.</summary>
    private void Sync(IRecordReader R)
    {
        var (deleted, created) = R.GetChanged<Asset>();

        foreach (var id in deleted)
            RemoveImageTile(id);
        foreach (var id in created)
            AddImageTile(id);
    }

    private void AddImageTile(SnowTag id)
    {
        var tileScene = GD.Load<PackedScene>(_tileScenePath);
        var tile = tileScene.Instantiate<ImageTile>();
        tile.Reference = id;
        tile.Clicked += OnTileClicked;
        _tileContainer.AddChild(tile);
        _tiles[id] = tile;
    }

    private void RemoveImageTile(SnowTag id)
    {
        if (!_tiles.Remove(id, out var tile))
            return;
        _tileContainer.RemoveChild(tile);
        tile.QueueFree();

        if (_selected == id)
            _selected = SnowTag.Empty;
    }
}

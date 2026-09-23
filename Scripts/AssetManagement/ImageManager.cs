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

    private Asset _selected;

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
        if (_selected != null)
            ProjectService.Instance.Upsert(_selected with { Deleted = true });
    }

    private void OnTileClicked(Asset target)
    {
        if (_selected != null && _tiles.TryGetValue(_selected.Id, out var previous))
        {
            previous.SetSelected(false);
        }

        _selected = target;

        if (_tiles.TryGetValue(_selected.Id, out var tile))
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

    private void AddImageTile(Asset asset)
    {
        var tileScene = GD.Load<PackedScene>(_tileScenePath);
        var tile = tileScene.Instantiate<ImageTile>();
        tile.SetAsset(asset);
        tile.Clicked += OnTileClicked;
        _tileContainer.AddChild(tile);
        _tiles[asset.Id] = tile;
    }

    private void Sync(IRecordReader R)
    {
        var assets = R.Get<Asset>();

        foreach (var asset in assets)
        {
            if (_tiles.TryGetValue(asset.Id, out var existing))
                existing.SetAsset(asset);
            else
                AddImageTile(asset);
        }

        var live = assets.Select(a => a.Id).ToHashSet();
        foreach (var id in _tiles.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _tiles[id].QueueFree();
            _tiles.Remove(id);
        }

        _selected = assets.FirstOrDefault(a => a.Id == _selected?.Id);
    }
}

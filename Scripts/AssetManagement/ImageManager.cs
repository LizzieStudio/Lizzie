using System.Collections.Generic;
using Godot;
using Lizzie.AssetManagement;

namespace Lizzie;

[View("images")]
public partial class ImageManager : Window, ICommandView
{
    private Button _backButton;
    private Button _addNewButton;
    private Button _removeButton;

    private PanelContainer _addPanel;
    private Button _addImageButton;
    private Button _cancelImageButton;

    public LineEdit _urlInput;
    public LineEdit _nameInput;

    private Control _library;
    private HFlowContainer _tileContainer;

    private Control _imageView;
    private TextureRect _image;
    private ShaderMaterial _frameGrid;

    private SpinBox _columns;
    private SpinBox _rows;
    private CheckBox _countSet;
    private SpinBox _count;

    private readonly Dictionary<SnowTag, ImageTile> _tiles = new();

    /// <summary>
    /// The image open in the view, or <see cref="SnowTag.Empty">.
    /// </summary>
    private SnowTag _open = SnowTag.Empty;

    /// <summary>
    /// The ImageAsset that <see cref="_image"/> was loaded for.
    /// </summary>
    private SnowTag _loaded = SnowTag.Empty;

    private const string _tileScenePath = "res://Scenes/Controls/image_tile.tscn";

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _backButton = GetNode<Button>("%BackButton");
        _backButton.Pressed += () => Open(SnowTag.Empty);
        _addNewButton = GetNode<Button>("%AddNewButton");
        _removeButton = GetNode<Button>("%RemoveButton");
        _addNewButton.Pressed += OnAddNewPressed;
        _removeButton.Pressed += OnRemovePressed;
        CloseRequested += QueueFree;

        _addPanel = GetNode<PanelContainer>("%AddImagePanel");
        _addImageButton = GetNode<Button>("%AddImageButton");
        _addImageButton.Pressed += OnAddImagePressed;
        _cancelImageButton = GetNode<Button>("%CancelImageButton");
        _cancelImageButton.Pressed += OnCancelImagePressed;

        _library = GetNode<Control>("%Library");
        _tileContainer = GetNode<HFlowContainer>("%TileContainer");

        _imageView = GetNode<Control>("%ImageView");
        _image = GetNode<TextureRect>("%Image");
        _frameGrid = (ShaderMaterial)_image.Material;

        _urlInput = GetNode<LineEdit>("%UrlInput");
        _nameInput = GetNode<LineEdit>("%NameInput");

        _columns = GetNode<SpinBox>("%Columns");
        var columnsEdits = _columns.GetNode<FocusEditGroup>("FocusEditGroup");
        _columns.ValueChanged += value =>
            columnsEdits.Update(
                ImageAsset.ById,
                _open,
                a => a with { Layout = a.Layout with { Columns = (int)value } }
            );

        _rows = GetNode<SpinBox>("%Rows");
        var rowsEdits = _rows.GetNode<FocusEditGroup>("FocusEditGroup");
        _rows.ValueChanged += value =>
            rowsEdits.Update(
                ImageAsset.ById,
                _open,
                a => a with { Layout = a.Layout with { Rows = (int)value } }
            );

        _countSet = GetNode<CheckBox>("%CountSet");
        _countSet.Toggled += on =>
            Atoms.R.Update(
                this,
                ImageAsset.ById,
                _open,
                a =>
                    a with
                    {
                        Layout = a.Layout with
                        {
                            Count = on ? a.Layout.Columns * a.Layout.Rows : null,
                        },
                    }
            );

        _count = GetNode<SpinBox>("%Count");
        var countEdits = _count.GetNode<FocusEditGroup>("FocusEditGroup");
        _count.ValueChanged += value =>
            countEdits.Update(
                ImageAsset.ById,
                _open,
                a => a with { Layout = a.Layout with { Count = (int)value } }
            );
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    private void OnRemovePressed()
    {
        if (Atoms.R.Get(ImageAsset.ById, _open) is { } image)
            Atoms.R.Write(this, Atoms.R.Unset(image));
    }

    /// <summary>
    /// For when the user clicks an image to see it,
    /// or <see cref="SnowTag.Empty"/> when they go back to the thumbnails.
    /// </summary>
    private void Open(SnowTag image)
    {
        // Ends the edits to the previous image's layout.
        GuiReleaseFocus();
        _open = image;
        Atoms.R.QueueSync(this);
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
        var asset = new ImageAsset
        {
            Id = SnowTag.Create(),
            Name = _nameInput.Text,
            CloudPath = _urlInput.Text,
        };

        Atoms.R.Write(this, asset);
        Open(asset.Id);

        _nameInput.Text = string.Empty;
        _urlInput.Text = string.Empty;
    }

    private void OnCancelImagePressed()
    {
        _addPanel.Visible = false;
        UpdateButtons(true);
    }

    private void Sync(IWatchReader R)
    {
        var (removed, added) = R.Diff(ImageAsset.All);

        foreach (var r in removed)
            RemoveImageTile(r.Id);
        foreach (var a in added)
            AddImageTile(a.Id);

        var open = R.Get(ImageAsset.ById, _open);
        _library.Visible = _addNewButton.Visible = open == null;
        _imageView.Visible = _backButton.Visible = _removeButton.Visible = open != null;
        Title = open == null ? "Images" : $"Images: {open.Name}";
        if (open == null)
            return;

        if (_loaded != open.Id)
            LoadImage(open);

        var layout = open.Layout;
        _frameGrid.SetShaderParameter("columns", layout.Columns);
        _frameGrid.SetShaderParameter("rows", layout.Rows);
        _frameGrid.SetShaderParameter("count", layout.FrameCount());
        _columns.SetValueNoSignal(layout.Columns);
        _rows.SetValueNoSignal(layout.Rows);
        _countSet.SetPressedNoSignal(layout.Count != null);
        _count.Editable = layout.Count != null;
        _count.SetValueNoSignal(layout.Count ?? layout.Columns * layout.Rows);
    }

    private async void LoadImage(ImageAsset asset)
    {
        _loaded = asset.Id;
        _image.Texture = null;
        var image = await AssetImageCache.Instance.FetchImageAsync(asset);
        // safety checks to make sure nothing changed during the load
        if (IsInstanceValid(this) && _loaded == asset.Id && image != null)
            _image.Texture = ImageTexture.CreateFromImage(image);
    }

    private void AddImageTile(SnowTag id)
    {
        var tileScene = GD.Load<PackedScene>(_tileScenePath);
        var tile = tileScene.Instantiate<ImageTile>();
        tile.Reference = id;
        tile.Clicked += Open;
        _tileContainer.AddChild(tile);
        _tiles[id] = tile;
    }

    private void RemoveImageTile(SnowTag id)
    {
        if (!_tiles.Remove(id, out var tile))
            return;
        _tileContainer.RemoveChild(tile);
        tile.QueueFree();

        if (_open == id)
            _open = SnowTag.Empty;
    }
}

using System;
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

    private const string _tileScenePath = "res://Scenes/Controls/image_tile.tscn";

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

        InitializeTiles();
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    public event EventHandler Closed;

    private void OnClosePressed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemovePressed() { }

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

        //TODO - Check that name and URL exist and are unique
        //plus other validation

        //create the new asset
        var asset = new Asset
        {
            Name = _nameInput.Text,
            Type = Asset.AssetType.Image,
            CloudPath = _urlInput.Text,
        };

        switch (_cloudServiceOption.Selected)
        {
            case 0:
                asset.ProviderType = CloudProviderType.GoogleDrive;
                break;
            case 1:
                asset.ProviderType = CloudProviderType.Dropbox;
                break;
            case 2:
                asset.ProviderType = CloudProviderType.OneDrive;
                break;
            default:
                GD.PrintErr("Invalid cloud service selected");
                return;
        }

        AddImageTile(asset);
        ProjectService.Instance.UpdateImage(asset);
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
        _tileContainer.AddChild(tile);

        _nameInput.Text = string.Empty;
        _urlInput.Text = string.Empty;
    }

    private void InitializeTiles()
    {
        foreach (var i in ProjectService.Instance.CurrentProject.Images)
        {
            AddImageTile(i.Value);
        }
    }
}

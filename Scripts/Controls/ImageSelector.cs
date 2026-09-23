using System;
using Godot;
using Lizzie.AssetManagement;

public partial class ImageSelector : Control
{
    private OptionButton _optionDropdown;
    private Button _imageEditorButton;
    private SnowTag _selected = SnowTag.Empty;

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _optionDropdown = GetNode<OptionButton>("%ImageList");
        _optionDropdown.ItemSelected += ItemSelected;

        _imageEditorButton = GetNode<Button>("%ImageManagerButton");
        _imageEditorButton.Pressed += ShowImageEditor;
    }

    private void ShowImageEditor() { }

    private void Sync(IRecordReader R)
    {
        _optionDropdown.Clear();
        _optionDropdown.AddItem("(none)", SnowTag.Empty);

        foreach (var asset in R.Get<Asset>())
            _optionDropdown.AddItem(asset.Name, asset.Id);

        UpdateSelection();
    }

    public void SetSelectedImage(SnowTag Id)
    {
        _selected = Id;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var index = _optionDropdown.GetItemIndex(_selected);
        _optionDropdown.Select(index >= 0 ? index : 0);
    }

    private void ItemSelected(long index)
    {
        _selected = _optionDropdown.GetItemId((int)index);
        ImageSelected?.Invoke(_selected);
    }

    public event Action<SnowTag> ImageSelected;
}

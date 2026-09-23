using System;
using System.Collections.Generic;
using Godot;
using Lizzie.AssetManagement;

public partial class ImageSelector : Control
{
    private OptionButton _optionDropdown;
    private Button _imageEditorButton;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        _optionDropdown = GetNode<OptionButton>("%ImageList");
        _optionDropdown.ItemSelected += ItemSelected;

        _imageEditorButton = GetNode<Button>("%ImageManagerButton");
        _imageEditorButton.Pressed += ShowImageEditor;

        ProjectService.Instance.Assets.Observe(UpdateAssets);
    }

    public override void _ExitTree()
    {
        ProjectService.Instance.Assets.Unobserve(UpdateAssets);
    }

    private void ShowImageEditor() { }

    private void UpdateAssets(IReadOnlyDictionary<SnowTag, Asset> records)
    {
        _optionDropdown.Clear();

        _optionDropdown.AddItem("(none)", 0);

        foreach (var (key, value) in records)
        {
            if (value.Deleted)
                continue;
            _optionDropdown.AddItem(value.Name, key);
        }
    }

    public void SetSelectedImage(SnowTag Id)
    {
        if (Id == SnowTag.Empty)
        {
            _optionDropdown.Select(0);
            return;
        }

        var index = _optionDropdown.GetItemIndex(Id);
        _optionDropdown.Select(index >= 0 ? index : 0);
    }

    private void ItemSelected(long index)
    {
        var id = _optionDropdown.GetItemId((int)index);

        ImageSelected?.Invoke(id);
    }

    public event Action<SnowTag> ImageSelected;
}

using System;
using System.Linq;
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

        if (_project != null)
            SetProjectLocal();
    }

    private void ShowImageEditor() { }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta) { }

    public void SetProject(Project project)
    {
        _project = project;
        if (IsNodeReady())
        {
            SetProjectLocal();
        }
    }

    private Project _project;

    private void SetProjectLocal()
    {
        _optionDropdown.Clear();

        _optionDropdown.AddItem("(none)", 0);

        if (_project == null)
        {
            return;
        }

        foreach (var i in _project.Assets)
        {
            if (i.Value.Deleted)
                continue;
            _optionDropdown.AddItem(i.Value.Name, i.Key.Value);
        }
    }

    public Asset SelectedImage
    {
        get
        {
            var id = _optionDropdown.GetSelectedId();
            if (id <= 0)
                return null;
            return ProjectService.Instance.CurrentProject?.GetImage(new SnowTag(id));
        }
        set
        {
            if (value == null)
            {
                _optionDropdown.Select(0);
                return;
            }

            var index = _optionDropdown.GetItemIndex(value.Id.Value);
            _optionDropdown.Select(index >= 0 ? index : 0);
        }
    }

    private void ItemSelected(long index)
    {
        var id = _optionDropdown.GetItemId((int)index);

        if (id == 0)
        {
            ImageSelected?.Invoke(this, new SelectedEventArgs<Asset>());
            return;
        }

        var a = ProjectService.Instance.CurrentProject?.GetImage(new SnowTag(id));
        ImageSelected?.Invoke(this, new SelectedEventArgs<Asset>(a));
    }

    public event EventHandler<SelectedEventArgs<Asset>> ImageSelected;
}

public class SelectedEventArgs<T> : EventArgs
{
    public SelectedEventArgs() { }

    public SelectedEventArgs(T item)
    {
        SelectedItem = item;
    }

    public T SelectedItem { get; set; }
}

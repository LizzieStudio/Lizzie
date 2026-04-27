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

        int index = 1;
        foreach (var i in _project.Images)
        {
            _optionDropdown.AddItem(i.Value.Name, index);
            _optionDropdown.SetItemMetadata(index, i.Key);
            index++;
        }
    }

    public Asset SelectedImage
    {
        get
        {
            var s = _optionDropdown.GetItemMetadata(_optionDropdown.Selected).ToString();

            if (string.IsNullOrEmpty(s))
            {
                return null;
            }
            var a = ProjectService.Instance.CurrentProject.Images.First(x => x.Key == s);
            return a.Value;
        }
        set
        {
            if (value == null)
            {
                _optionDropdown.Select(0);
                return;
            }

            var key = _project?.Images.FirstOrDefault(x => x.Value.AssetId == value.AssetId).Key;

            for (int i = 0; i < _optionDropdown.ItemCount; i++)
            {
                if (_optionDropdown.GetItemMetadata(i).ToString() == key)
                {
                    _optionDropdown.Select(i);
                    return;
                }
            }

            _optionDropdown.Select(0);
        }
    }

    private void ItemSelected(long index)
    {
        var s = _optionDropdown.GetItemMetadata((int)index).ToString();

        if (string.IsNullOrEmpty(s))
        {
            ImageSelected?.Invoke(this, new SelectedEventArgs<Asset>());
        }
        var a = ProjectService.Instance.CurrentProject.Images.First(x => x.Key == s);

        ImageSelected?.Invoke(this, new SelectedEventArgs<Asset>(a.Value));
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

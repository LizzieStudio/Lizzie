using System;
using Godot;

public partial class DataSetSelector : OptionButton
{
    private SnowTag _selected = SnowTag.Empty;

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    public override void _Ready()
    {
        ItemSelected += OnItemSelected;
    }

    private void Sync(IRecordReader R)
    {
        Clear();
        AddItem("(none)", SnowTag.Empty.Value);

        foreach (var d in R.Get<DataSet>())
        {
            AddItem(d.Name, d.Id.Value);
        }

        UpdateSelection();
    }

    public SnowTag SelectedDataSet
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        var index = GetItemIndex(_selected);
        if (index < 0)
            index = 0;
        Select(index);
    }

    private void OnItemSelected(long index)
    {
        _selected = new SnowTag(GetItemId((int)index));
        DataSetSelected?.Invoke(_selected);
    }

    public event Action<SnowTag> DataSetSelected;
}

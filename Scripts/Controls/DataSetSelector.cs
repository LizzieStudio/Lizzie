using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class DataSetSelector : OptionButton
{
    private SnowTag _selected = SnowTag.Empty;

    public override void _Ready()
    {
        ItemSelected += OnItemSelected;

        ProjectService.Instance.DataSets.Observe(UpdateDataSets);
    }

    public override void _ExitTree()
    {
        ProjectService.Instance.DataSets.Unobserve(UpdateDataSets);
    }

    private void UpdateDataSets(IReadOnlyDictionary<SnowTag, DataSet> datasets)
    {
        Clear();
        AddItem("(none)", SnowTag.Empty.Value);

        foreach (var d in datasets.Values.Where(v => !v.Deleted))
        {
            AddItem(d.Name, d.Id.Value);
        }

        SelectItem(_selected);
    }

    public SnowTag SelectedDataSet
    {
        get => Selected < 0 ? SnowTag.Empty : new SnowTag(GetSelectedId());
        set
        {
            _selected = value;
            SelectItem(value);
        }
    }

    private void SelectItem(SnowTag id)
    {
        var index = id == SnowTag.Empty ? -1 : GetItemIndex(id.Value);
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

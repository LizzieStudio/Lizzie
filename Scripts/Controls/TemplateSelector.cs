using System;
using Godot;

public partial class TemplateSelector : OptionButton
{
    private SnowTag _selected = SnowTag.Empty;
    private Template.TemplateTarget _target = Template.TemplateTarget.Flat;

    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    public override void _Ready()
    {
        ItemSelected += OnItemSelected;
    }

    /// <summary>
    /// Only templates for this target are listed.
    /// </summary>
    public Template.TemplateTarget Target
    {
        get => _target;
        set
        {
            if (_target != value)
            {
                _target = value;
                ProjectService.Instance.QueueSync(this);
            }
        }
    }

    private void Sync(IRecordReader R)
    {
        Clear();
        AddItem("(none)", SnowTag.Empty);

        foreach (var t in R.Get<Template>(t => t.Target == _target))
        {
            AddItem(t.Name, t.Id);
        }

        UpdateSelection();
    }

    public SnowTag SelectedTemplate
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
        _selected = GetItemId((int)index);
        TemplateSelected?.Invoke(_selected);
    }

    public event Action<SnowTag> TemplateSelected;
}

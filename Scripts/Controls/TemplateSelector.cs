using System;
using System.Linq;
using Godot;

namespace Lizzie;

public partial class TemplateSelector : OptionButton
{
    private SnowTag _selected = SnowTag.Empty;
    private Func<Template, bool> _shows = t => t.Sides == 0;

    public override void _EnterTree()
    {
        AtomService.Instance.Watch(this, Sync);
    }

    public override void _Ready()
    {
        ItemSelected += OnItemSelected;
    }

    /// <summary>
    /// Filters which templates are listed. Shows printed templates by default.
    /// </summary>
    public Func<Template, bool> Shows
    {
        get => _shows;
        set
        {
            _shows = value;
            AtomService.Instance.QueueSync(this);
        }
    }

    private void Sync(IWatchReader R)
    {
        Clear();
        AddItem("(none)", SnowTag.Empty);

        foreach (var t in R.Get(Template.All).Where(_shows))
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

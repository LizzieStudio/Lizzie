using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class TemplateSelector : OptionButton
{
    private SnowTag _selected = SnowTag.Empty;
    private Template.TemplateTarget _target = Template.TemplateTarget.Flat;

    public override void _Ready()
    {
        ItemSelected += OnItemSelected;

        ProjectService.Instance.Templates.Observe(UpdateTemplates);
    }

    public override void _ExitTree()
    {
        ProjectService.Instance.Templates.Unobserve(UpdateTemplates);
    }

    /// <summary>
    /// Only templates for this target are listed.
    /// </summary>
    public Template.TemplateTarget Target
    {
        get => _target;
        set
        {
            if (_target == value)
                return;
            _target = value;
            if (IsInsideTree())
                UpdateTemplates(ProjectService.Instance.Templates.Records);
        }
    }

    private void UpdateTemplates(IReadOnlyDictionary<SnowTag, Template> templates)
    {
        Clear();
        AddItem("(none)", SnowTag.Empty.Value);

        foreach (var t in templates.Values.Where(v => !v.Deleted && v.Target == _target))
        {
            AddItem(t.Name, t.Id.Value);
        }

        SelectItem(_selected);
    }

    public SnowTag SelectedTemplate
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
        TemplateSelected?.Invoke(_selected);
    }

    public event Action<SnowTag> TemplateSelected;
}

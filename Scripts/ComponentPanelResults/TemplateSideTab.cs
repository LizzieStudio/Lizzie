using System;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Edits a <see cref="TemplateSide"/>.
/// </summary>
public partial class TemplateSideTab : SideTab
{
    [Export]
    private AtomSelector _template;

    [Export]
    private Button _editTemplate;

    [Export]
    private AtomSelector _dataset;

    [Export]
    private Button _editDataset;

    [Export]
    private AtomSelector _quantity;

    private readonly WatchableValue<TemplateSide> _side = new() { Value = new() };
    private readonly WatchableValue<Func<Template, bool>> _showsTemplates = new()
    {
        Value = t => t.Sides == 0,
    };

    public override void _Ready()
    {
        _template.List(
            R => R.Get(Template.All).Where(R.Get(_showsTemplates)).Select(t => (t.Id, t.Name)),
            new(R => R.Get(_side).TemplateRef, id => Edit(s => s with { TemplateRef = id }))
        );
        _dataset.List(
            R => R.Get(Dataset.All).Select(d => (d.Id, d.Name)),
            new(
                R => R.Get(_side).DatasetRef,
                id => Edit(s => s with { DatasetRef = id, QuantityRef = Ref<DataColumn>.Empty })
            )
        );
        _quantity.List(
            R => R.Get(DataColumn.ByDataset, R.Get(_side).DatasetRef).Select(c => (c.Id, c.Name)),
            new(R => R.Get(_side).QuantityRef, id => Edit(s => s with { QuantityRef = id }))
        );
        _editTemplate.Pressed += () =>
            EventBus.Instance.Publish(
                new ShowTemplateEditor { TemplateRef = _side.Value.TemplateRef }
            );
        _editDataset.Pressed += () =>
            EventBus.Instance.Publish(
                new ShowDatasetEditor { DatasetRef = _side.Value.DatasetRef }
            );
    }

    private void Edit(Func<TemplateSide, TemplateSide> edit)
    {
        _side.Value = edit(_side.Value);
        RaiseChanged();
    }

    /// <summary>
    /// Filters which templates are listed. Printed templates are listed by default.
    /// If a selected template is filtered, it's deselected.
    /// </summary>
    public Func<Template, bool> ShowsTemplates
    {
        set
        {
            _showsTemplates.Value = value;
            if (
                Atoms.R.Get(Template.ById, _side.Value.TemplateRef) is { } template
                && !value(template)
            )
                _side.Value = _side.Value with { TemplateRef = Ref<Template>.Empty };
        }
    }

    public override SideParameters Side => _side.Value;

    public override bool TryLoad(SideParameters side)
    {
        if (side is not TemplateSide template)
            return false;
        _side.Value = template;
        return true;
    }
}

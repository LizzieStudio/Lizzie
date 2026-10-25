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

    private SnowTag _templateRef = SnowTag.Empty;
    private SnowTag _datasetRef = SnowTag.Empty;
    private SnowTag _quantityRef = SnowTag.Empty;

    public override void _Ready()
    {
        ShowsTemplates = t => t.Sides == 0;
        _dataset.Items = R => R.Get(Dataset.All).Select(d => (d.Name, d.Id));
        _template.AtomSelected += id =>
        {
            _templateRef = id;
            RaiseChanged();
        };
        _dataset.AtomSelected += id =>
        {
            _datasetRef = id;
            _quantityRef = SnowTag.Empty;
            ShowColumns(id);
            _quantity.SelectedRef = SnowTag.Empty;
            RaiseChanged();
        };
        _quantity.AtomSelected += id =>
        {
            _quantityRef = id;
            RaiseChanged();
        };
        _editTemplate.Pressed += () =>
            EventBus.Instance.Publish(new ShowTemplateEditor { TemplateRef = _templateRef });
        _editDataset.Pressed += () =>
            EventBus.Instance.Publish(new ShowDatasetEditor { DatasetRef = _datasetRef });
    }

    /// <summary>
    /// Filters which templates are listed. Printed templates are listed by default.
    /// If a selected template is filtered, it's deselected.
    /// </summary>
    public Func<Template, bool> ShowsTemplates
    {
        set
        {
            _template.Items = R => R.Get(Template.All).Where(value).Select(t => (t.Name, t.Id));
            if (Atoms.R.Get(Template.ById, _templateRef) is { } template && !value(template))
            {
                _templateRef = SnowTag.Empty;
                _template.SelectedRef = SnowTag.Empty;
            }
        }
    }

    public override SideParameters Side =>
        new TemplateSide
        {
            TemplateRef = _templateRef,
            DatasetRef = _datasetRef,
            QuantityRef = _quantityRef,
        };

    public override bool TryLoad(SideParameters side)
    {
        if (side is not TemplateSide template)
            return false;
        _templateRef = template.TemplateRef;
        _template.SelectedRef = template.TemplateRef;
        _datasetRef = template.DatasetRef;
        _dataset.SelectedRef = template.DatasetRef;
        _quantityRef = template.QuantityRef;
        ShowColumns(template.DatasetRef);
        _quantity.SelectedRef = template.QuantityRef;
        return true;
    }

    private void ShowColumns(SnowTag dataset) =>
        _quantity.Items = R => R.GetColumns(dataset).Select(c => (c.Name, c.Id));
}

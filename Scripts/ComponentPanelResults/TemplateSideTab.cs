using System;
using Godot;

/// <summary>
/// Edits a <see cref="TemplateSide"/>.
/// </summary>
public partial class TemplateSideTab : SideTab
{
    [Export]
    private TemplateSelector _template;

    [Export]
    private Button _editTemplate;

    [Export]
    private DataSetSelector _dataset;

    [Export]
    private Button _editDataset;

    private SnowTag _templateRef = SnowTag.Empty;
    private SnowTag _datasetRef = SnowTag.Empty;

    public override void _Ready()
    {
        _template.TemplateSelected += id =>
        {
            _templateRef = id;
            RaiseChanged();
        };
        _dataset.DataSetSelected += id =>
        {
            _datasetRef = id;
            RaiseChanged();
        };
        _editTemplate.Pressed += () =>
            EventBus.Instance.Publish(new ShowTemplateEditor { TemplateRef = _templateRef });
        _editDataset.Pressed += () =>
            EventBus.Instance.Publish(new ShowDatasetEditor { DatasetRef = _datasetRef });
    }

    /// <summary>
    /// Filters which templates are listed.
    /// If a selected template is filtered, it's deselected.
    /// </summary>
    public Func<Template, bool> ShowsTemplates
    {
        set
        {
            _template.Shows = value;
            if (
                AtomService.Instance.Get(Template.ById, _templateRef) is { } template
                && !value(template)
            )
            {
                _templateRef = SnowTag.Empty;
                _template.SelectedTemplate = SnowTag.Empty;
            }
        }
    }

    public override SideParameters Side =>
        new TemplateSide { Template = _templateRef, Dataset = _datasetRef };

    public override bool TryLoad(SideParameters side)
    {
        if (side is not TemplateSide template)
            return false;
        _templateRef = template.Template;
        _template.SelectedTemplate = template.Template;
        _datasetRef = template.Dataset;
        _dataset.SelectedDataSet = template.Dataset;
        return true;
    }
}

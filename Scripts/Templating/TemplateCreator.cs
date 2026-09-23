using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Godot;
using Lizzie.Scripts.Templating;

public partial class TemplateCreator : Window
{
    [Export]
    private TextureRect _preview;

    [Export]
    private Texture2D _d4_overlay;

    [Export]
    private Texture2D _d6_overlay;

    [Export]
    private Texture2D _d8_overlay;

    [Export]
    private Texture2D _d10_overlay;

    [Export]
    private Texture2D _d12_overlay;

    [Export]
    private Texture2D _d20_overlay;

    private Tree _elementTree;
    private VBoxContainer _paramContainer;
    private TextureRect _previewOverlay;
    private HBoxContainer _sizeControls;
    private HBoxContainer _overlayControls;
    private Button _showOverlayButton;

    private Button _textButton;
    private Button _closeButton;
    private Button _imageButton;

    private MenuButton _otherElementButton;
    private PopupMenu _otherElementPopup;

    private Button _deleteElementButton;
    private Button _renameElementButton;
    private Button _duplicateElementButton;

    private BoundsRect _boundsRect;

    private PackedScene _stringParam;
    private PackedScene _numberParam;
    private PackedScene _colorParam;
    private PackedScene _anchorParam;
    private PackedScene _boolParam;
    private PackedScene _horJustifyParam;
    private PackedScene _verJustifyParam;
    private PackedScene _imageParam;
    private PackedScene _trackTypeParam;

    private ITemplateElement _selectedElement;
    private TreeItem _rootItem;

    private List<ITemplateElement> _templateElements = new();
    private TextureContext _textureContext = new();

    private OptionButton _templateNameSelector;

    private OptionButton _cardSizes;
    private LineEdit _heightInput;
    private LineEdit _widthInput;

    private Timer _updateTimer;
    private bool _updateRequired;

    private Button _newButton;
    private Button _saveButton;
    private Button _duplicateButton;
    private Button _zoomButton;
    private Panel _previewWindow;
    private DataSetSelector _dataSetSelector;

    private PageControl _pageControl;

    private AcceptDialog _acceptDialog;
    private ConfirmationDialog _saveBeforeCloseDialog;

    /// <summary>True when the current template has edits that haven't been saved.</summary>
    private bool _hasUnsavedChanges;

    // Called when the node enters the scene tree for the first time.
    public override void _EnterTree()
    {
        ProjectService.Instance.Watch(this, Sync);
    }

    private void Sync(IRecordReader R)
    {
        LoadTemplateNameSelector(R);
        _templateNameSelector.Select(
            _templateNameSelector.GetItemIndex(CurrentTemplate?.Id ?? SnowTag.Empty)
        );
    }

    public override void _Ready()
    {
        InitPreview();

        InitToolbar();

        InitElementTree();

        InitParamTypes();

        InitializeNewTemplateDialog();

        _textureContext.ParentSize = _preview.GetSize();

        InitializeCardFit();

        _acceptDialog = (AcceptDialog)GetNode("ConfirmationDialog");

        InitializeSaveBeforeCloseDialog();

        this.VisibilityChanged += UpdateScrollBarVisibility;
        this.CloseRequested += RequestClose;

        UpdateProject();

        if (_tempTemplateRef != SnowTag.Empty)
        {
            SetTemplateById(_tempTemplateRef);
        }
    }

    public override void _Process(double delta)
    {
        if (_fitRequired)
        {
            OnStandardSizeChanged(0);
        }
    }

    private void InitToolbar()
    {
        _templateNameSelector = GetNode<OptionButton>("%TemplateName");
        _templateNameSelector.ItemSelected += ChangeTemplate;

        _newButton = GetNode<Button>("%NewButton");
        _newButton.Pressed += () => _newTemplateDialog.Show();

        _saveButton = GetNode<Button>("%SaveButton");
        _saveButton.Pressed += SaveTemplate;

        _duplicateButton = GetNode<Button>("%DuplicateButton");

        _closeButton = GetNode<Button>("%CloseButton");
        _closeButton.Pressed += RequestClose;

        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += HeightWidthChange;
        _heightInput.TextChanged += _ => SetHasUnsavedChanges();

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += HeightWidthChange;
        _widthInput.TextChanged += _ => SetHasUnsavedChanges();

        _sizeControls = GetNode<HBoxContainer>("%SizeControls");
        _overlayControls = GetNode<HBoxContainer>("%OverlayControls");
        _showOverlayButton = GetNode<Button>("%ShowOverlayToggle");
        _showOverlayButton.Pressed += OnOverlayToggle;

        _cardSizes = GetNode<OptionButton>("%StandardSize");
        _cardSizes.ItemSelected += OnStandardSizeChanged;
        _cardSizes.ItemSelected += _ => SetHasUnsavedChanges();
        InitializeStandardSizes();
        OnStandardSizeChanged(0);

        _dataSetSelector = GetNode<DataSetSelector>("%Dataset");
        _dataSetSelector.DataSetSelected += OnDatasetChanged;
        _dataSetSelector.DataSetSelected += _ => SetHasUnsavedChanges();

        _pageControl = GetNode<PageControl>("%PageControl");
        _pageControl.Hide();
        _pageControl.ItemSelected += ChangePage;
    }

    private void RequestClose()
    {
        if (!_hasUnsavedChanges)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _saveBeforeCloseDialog.PopupCentered();
    }

    /// <summary>Flags the current template as having unsaved edits.</summary>
    private void SetHasUnsavedChanges()
    {
        if (CurrentTemplate == null)
            return;

        _hasUnsavedChanges = true;
    }

    private void InitializeSaveBeforeCloseDialog()
    {
        _saveBeforeCloseDialog = new ConfirmationDialog();
        _saveBeforeCloseDialog.DialogText = "Do you want to save changes before closing?";
        _saveBeforeCloseDialog.Title = "Save Changes";
        _saveBeforeCloseDialog.OkButtonText = "Save and Close";
        _saveBeforeCloseDialog.CancelButtonText = "Cancel";

        // Add a "Don't Save" button
        _saveBeforeCloseDialog.AddButton("Don't Save", false, "dont_save");

        // Connect signals
        _saveBeforeCloseDialog.Confirmed += OnSaveAndClose;
        _saveBeforeCloseDialog.Canceled += OnCancelClose;
        _saveBeforeCloseDialog.CustomAction += OnCustomAction;

        AddChild(_saveBeforeCloseDialog);
    }

    private void OnSaveAndClose()
    {
        SaveTemplate();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClose()
    {
        // Do nothing - just close the dialog, keep the window open
    }

    private void OnCustomAction(StringName action)
    {
        if (action == "dont_save")
        {
            // Close without saving
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler Closed;

    private Template _currentTemplate;

    /// <summary>
    /// Switches the edited template and rebuilds the whole editor UI from it.
    /// Edits made from within the editor must go through <see cref="EditCurrentTemplate"/> instead.
    /// </summary>
    public Template CurrentTemplate
    {
        get => _currentTemplate;
        set
        {
            _currentTemplate = value;
            MapTemplate();
        }
    }

    /// <summary>Replaces the current template without rebuiling the editor UI.</summary>
    private void EditCurrentTemplate(Func<Template, Template> edit)
    {
        if (_currentTemplate != null)
            _currentTemplate = edit(_currentTemplate);
    }

    private SnowTag _tempTemplateRef = SnowTag.Empty;

    public void SetTemplateById(SnowTag id)
    {
        if (id == SnowTag.Empty)
            return;

        if (!IsNodeReady())
        {
            _tempTemplateRef = id;
            return;
        }

        var idx = _templateNameSelector.GetItemIndex(id.Value);
        if (idx >= 0)
        {
            _templateNameSelector.Selected = idx;
            ChangeTemplate(idx);
        }
    }

    private void MapTemplate()
    {
        try
        {
            MapTemplateInternal();
        }
        finally
        {
            _hasUnsavedChanges = false;
        }
    }

    private void MapTemplateInternal()
    {
        if (CurrentTemplate == null)
        {
            _elementTree.Clear();
            _rootItem = _elementTree.CreateItem();
            _templateElements.Clear();
            ClearParameterBox();
            return;
        }

        //change sizes
        var size = CurrentTemplate.SizeTemplate;

        if (!string.IsNullOrEmpty(size))
        {
            for (int i = 0; i < _cardSizes.ItemCount; i++)
            {
                if (_cardSizes.GetItemText(i) == size)
                {
                    _cardSizes.Select(i);
                    OnStandardSizeChanged(i);
                    break;
                }
            }
        }

        //set up element tree
        _elementTree.Clear();
        _rootItem = _elementTree.CreateItem(); //create root item

        _templateElements.Clear();

        ClearParameterBox();

        foreach (var t in CurrentTemplate.Elements)
        {
            var te = TemplateEngine.BuildTemplateElement(t);

            var ni = _elementTree.CreateItem(_rootItem);

            te.Id = Snowport.Clock.CreateTag();
            ni.SetMetadata(0, te.Id.Value);
            ni.SetText(0, te.ElementName);

            _templateElements.Add(te);
        }

        //dataset
        MapDataset();

        MapTreeToElements();

        //update preview
        _updateRequired = true;
    }

    private void MapTreeToElements()
    {
        _hierarchicalElements.Clear();

        var r = _elementTree.GetRoot();

        foreach (var item in r.GetChildren())
        {
            var parent = GetElementByTreeItem(item);
            _hierarchicalElements.Add(parent);

            MapChildrenToElements(item, parent);
        }
    }

    private void MapChildrenToElements(TreeItem item, ITemplateElement parent)
    {
        parent.Children.Clear();

        foreach (var c in item.GetChildren())
        {
            var child = GetElementByTreeItem(c);
            parent.Children.Add(child);

            MapChildrenToElements(c, child);
        }
    }

    private List<ITemplateElement> _hierarchicalElements = new();

    private void ChangeTemplate(long index)
    {
        if (index < 0 || index >= _templateNameSelector.ItemCount)
            return;

        var switched = ProjectService.Instance.Get<Template>(
            _templateNameSelector.GetItemId((int)index)
        );
        if (switched != null)
        {
            SaveTemplate(); //save current template before switching
            CurrentTemplate = switched;
        }
    }

    private void SaveTemplate()
    {
        if (CurrentTemplate == null)
            return;

        EditCurrentTemplate(t =>
            t with
            {
                SizeTemplate = _curSizeType,
                Width = _curWidth,
                Height = _curHeight,
                Elements = TemplateEngine
                    .MapTemplateElementsToProjectFormat(_hierarchicalElements)
                    .Select(d => d.ToImmutableDictionary())
                    .ToImmutableArray(),
            }
        );
        ProjectService.Instance.Upsert(_currentTemplate);

        _hasUnsavedChanges = false;
    }

    private ScrollBar _previewHScroll;

    private ScrollBar _previewVScroll;

    private void InitPreview()
    {
        _boundsRect = GetNode<BoundsRect>("%BoundsRect");
        _boundsRect.Hide();
        _boundsRect.BoundsChanged += BoundsChanged;

        _updateTimer = GetNode<Timer>("Timer");
        _updateTimer.Timeout += UpdateTimerExpired;
        _updateTimer.Start();

        _previewWindow = GetNode<Panel>("%PreviewWindow");
        _windowSize = _previewWindow.GetSize();

        _previewOverlay = GetNode<TextureRect>("%PreviewOverlay");
        _previewOverlay.Visible = false;

        _previewHScroll = GetNode<ScrollBar>("%PreviewHScroll");
        _previewHScroll.ValueChanged += OnScroll;

        _previewVScroll = GetNode<ScrollBar>("%PreviewVScroll");
        _previewVScroll.ValueChanged += OnScroll;

        _zoomInButton = GetNode<Button>("%ZoomIn");
        _zoomInButton.Pressed += ZoomIn;
        _zoomOutButton = GetNode<Button>("%ZoomOut");
        _zoomOutButton.Pressed += ZoomOut;
        _zoomFitButton = GetNode<Button>("%ZoomFit");
        _zoomFitButton.Pressed += ZoomFit;

        UpdateScrollBarVisibility();
    }

    private void InitElementTree()
    {
        _elementTree = GetNode<Tree>("%TemplateTree");
        _paramContainer = GetNode<VBoxContainer>("%TemplateParams");
        _textButton = GetNode<Button>("%TextButton");
        _textButton.Pressed += AddText;

        _imageButton = GetNode<Button>("%ImageButton");
        _imageButton.Pressed += AddImage;

        _otherElementButton = GetNode<MenuButton>("%OtherElements");
        _otherElementPopup = _otherElementButton.GetPopup();
        _otherElementPopup.IndexPressed += OnOtherElementSelected;
        _rootItem = _elementTree.CreateItem(); //create root item
        _elementTree.ItemSelected += TreeItemSelected;

        _deleteElementButton = GetNode<Button>("%DeleteElement");
        _deleteElementButton.Pressed += DeleteCurrentElementQuery;

        _renameElementButton = GetNode<Button>("%RenameElement");
        _renameElementButton.Pressed += RenameCurrentElement;

        _duplicateElementButton = GetNode<Button>("%DuplicateElement");
        _duplicateElementButton.Pressed += DuplicateCurrentElement;

        EnableTreeDragAndDrop();
    }

    private void InitParamTypes()
    {
        _stringParam = GD.Load<PackedScene>("res://Scenes/Templating/StringParam.tscn");
        _numberParam = GD.Load<PackedScene>("res://Scenes/Templating/NumericParam.tscn");
        _colorParam = GD.Load<PackedScene>("res://Scenes/Templating/ColorParam.tscn");
        _anchorParam = GD.Load<PackedScene>("res://Scenes/Templating/AnchorParam.tscn");
        _boolParam = GD.Load<PackedScene>("res://Scenes/Templating/BooleanParam.tscn");
        _horJustifyParam = GD.Load<PackedScene>("res://Scenes/Templating/HorJustifyParam.tscn");
        _verJustifyParam = GD.Load<PackedScene>("res://Scenes/Templating/VerJustifyParam.tscn");
        _imageParam = GD.Load<PackedScene>("res://Scenes/Templating/ImageParam.tscn");
        _trackTypeParam = GD.Load<PackedScene>("res://Scenes/Templating/TrackTypeParam.tscn");
    }

    private void HeightWidthChange(string newtext)
    {
        InitializeCardFit();
        _updateRequired = true;
    }

    public IconLibrary IconLibrary { get; set; } = new();

    private void UpdateTimerExpired()
    {
        if (_updateRequired)
        {
            _updateRequired = false;
            UpdateTexture(false, false);
        }
    }

    private void BoundsChanged(object sender, EventArgs e)
    {
        if (_selectedElement == null)
            return;

        if (_textureContext.ParentSize.X == 0 || _textureContext.ParentSize.Y == 0)
            return;

        SetHasUnsavedChanges();

        var m = _boundsRect.GetBounds();

        int w = (int)_textureContext.ParentSize.X - m.l - m.r;
        int h = (int)_textureContext.ParentSize.Y - m.t - m.b;

        float scaleX = 100 / _textureContext.ParentSize.X;
        float scaleY = 100 / _textureContext.ParentSize.Y;

        UpdateParamControl(
            "X",
            ((int)(scaleX * (m.l + w / 2f))).ToString(CultureInfo.InvariantCulture)
        );
        UpdateParamControl(
            "Y",
            ((int)(scaleY * (m.t + h / 2f))).ToString(CultureInfo.InvariantCulture)
        );
        UpdateParamControl("Width", ((int)(scaleX * w)).ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Height", ((int)(scaleY * h)).ToString(CultureInfo.InvariantCulture));

        _updateRequired = true;
    }

    private void UpdateParamControl(string name, string value)
    {
        var p = GetParamControl(name);
        if (p != null)
            p.UpdateParameter(value);

        if (_selectedElement != null)
        {
            _selectedElement.SetParameterValue(name, value);
        }
    }

    private IParamControl GetParamControl(string name)
    {
        foreach (var node in _paramContainer.GetChildren())
        {
            if (node is IParamControl pc && pc.GetParameter().Name == name)
            {
                return pc;
            }
        }

        return null;
    }

    private void TreeItemSelected()
    {
        var p = GetElementByTreeItem(_elementTree.GetSelected());
        if (p != null)
        {
            _selectedElement = p;
            RemapParameters();
            _boundsRect.Show();
            UpdateBoundsRect();
        }
    }

    private ITemplateElement GetElementByTreeItem(TreeItem ti)
    {
        var id = ti.GetMetadata(0).AsInt32();

        //matching param
        var p = _templateElements.FirstOrDefault(x => x.Id.Value == id);
        return p;
    }

    #region Element Tools

    private void DeleteCurrentElementQuery()
    {
        if (_selectedElement == null)
            return;
        var ti = _elementTree.GetSelected();

        if (ti.GetMetadata(0).AsInt32() != _selectedElement.Id.Value)
            return;

        _acceptDialog.DialogText =
            $"Are you sure you want to delete element {_selectedElement.ElementName}?";
        _acceptDialog.Confirmed += DeleteCurrentElement;
        _acceptDialog.Show();
    }

    private void DeleteCurrentElement()
    {
        var ti = _elementTree.GetSelected();

        _templateElements.Remove(_selectedElement);

        var element = GetElementByTreeItem(ti);
        var parent = ti.GetParent();
        parent?.RemoveChild(ti);
        ti.Free();

        ClearParameterBox();

        MapTreeToElements();
        SetHasUnsavedChanges();
        _updateRequired = true;
    }

    private void RenameCurrentElement()
    {
        if (_selectedElement == null)
            return;
        var ti = _elementTree.GetSelected();
        ti.SetEditable(0, true);
        SetHasUnsavedChanges();
    }

    private void DuplicateCurrentElement()
    {
        if (_selectedElement == null)
            return;

        var id = Snowport.Clock.CreateTag();

        TemplateElement t = new TextElement(); //for now - delete once all switch elements are populated

        switch (_selectedElement.ElementType)
        {
            case ITemplateElement.TemplateElementType.Text:
                t = new TextElement();
                break;
            case ITemplateElement.TemplateElementType.Box:
                break;
            case ITemplateElement.TemplateElementType.Image:
                t = new ImageElement();
                break;
            case ITemplateElement.TemplateElementType.Note:
                break;
            case ITemplateElement.TemplateElementType.Circle:
                break;
            case ITemplateElement.TemplateElementType.Polygon:
                break;
            case ITemplateElement.TemplateElementType.Table:
                break;
            case ITemplateElement.TemplateElementType.Line:
                break;
            case ITemplateElement.TemplateElementType.Container:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var elementName = CreateUniqueElementName(_selectedElement.ElementName);

        var ni = _elementTree.CreateItem(_rootItem); //update so that it places item as sibling to selected
        ni.SetMetadata(0, id.Value);
        ni.SetText(0, elementName);

        t.Id = id;
        t.ElementName = elementName;

        t.Parameters.Clear();

        foreach (var e in _selectedElement.Parameters)
        {
            t.Parameters.Add(
                new TemplateParameter
                {
                    Name = e.Name,
                    Value = e.Value,
                    Type = e.Type,
                }
            );
        }

        _templateElements.Add(t);
        { }

        _elementTree.SetSelected(ni, 0);

        MapTreeToElements();
        SetHasUnsavedChanges();
    }

    #endregion

    private void ClearParameterBox()
    {
        foreach (var p in _paramContainer.GetChildren())
        {
            if (p is IParamControl pc)
                pc.ParameterUpdated -= OnTextureUpdate;
            p.QueueFree();
        }

        _selectedElement = null;
        _boundsRect.Hide();
    }

    private void OnOtherElementSelected(long index)
    {
        if (index == 0)
        {
            AddTextureElement(ITemplateElement.TemplateElementType.Frame);
        }

        if (index == 1)
        {
            AddTextureElement(ITemplateElement.TemplateElementType.Track);
        }
        UpdateTexture(true, true);
    }

    private void AddTextureElement(ITemplateElement.TemplateElementType type)
    {
        var id = Snowport.Clock.CreateTag();

        TemplateElement t = new TextElement();

        string prefix = type.ToString();

        switch (type)
        {
            case ITemplateElement.TemplateElementType.Text:
                t = new TextElement();
                break;

            case ITemplateElement.TemplateElementType.Box:
                break;
            case ITemplateElement.TemplateElementType.Image:
                t = new ImageElement();
                break;
            case ITemplateElement.TemplateElementType.Note:
                break;
            case ITemplateElement.TemplateElementType.Circle:
                break;
            case ITemplateElement.TemplateElementType.Polygon:
                break;
            case ITemplateElement.TemplateElementType.Table:
                break;
            case ITemplateElement.TemplateElementType.Line:
                break;
            case ITemplateElement.TemplateElementType.Container:
                break;
            case ITemplateElement.TemplateElementType.Frame:
                t = new FrameElement();
                break;
            case ITemplateElement.TemplateElementType.Track:
                t = new TrackElement();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        var elementName = CreateUniqueElementName(prefix);

        var ni = _elementTree.CreateItem(_rootItem);
        ni.SetMetadata(0, id.Value);
        ni.SetText(0, elementName);

        t.Id = id;
        t.ElementName = elementName;
        _templateElements.Add(t);

        _elementTree.SetSelected(ni, 0);

        MapTreeToElements();
        SetHasUnsavedChanges();
    }

    /// <summary>
    /// Takes <paramref name="prefix"/> and adds an increment to make it unique.
    /// </summary>
    private string CreateUniqueElementName(string prefix)
    {
        var names = _templateElements.Select(e => e.ElementName).ToHashSet();

        for (int i = 2; i < int.MaxValue; i++)
        {
            var candidate = $"{prefix}{i}";
            if (!names.Contains(candidate))
                return candidate;
        }

        throw new Exception("unique name not found");
    }

    private void RemapParameters()
    {
        if (_selectedElement == null)
            return;

        ClearParameters();

        foreach (var p in _selectedElement.Parameters)
        {
            HBoxContainer t;

            switch (p.Type)
            {
                case TemplateParameter.TemplateParameterType.Text:
                    t = _stringParam.Instantiate<NumericParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Number:
                    t = _numberParam.Instantiate<NumericParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Color:
                    t = _colorParam.Instantiate<ColorParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Anchor:
                    t = _anchorParam.Instantiate<ListParamControl>();
                    break;
                case TemplateParameter.TemplateParameterType.Boolean:
                    t = _boolParam.Instantiate<BooleanParamControl>();
                    break;

                case TemplateParameter.TemplateParameterType.HorizontalAlignment:
                    t = _horJustifyParam.Instantiate<PopupParamControl>();
                    break;

                case TemplateParameter.TemplateParameterType.VerticalAlignment:
                    t = _verJustifyParam.Instantiate<PopupParamControl>();
                    break;

                case TemplateParameter.TemplateParameterType.Image:
                    t = _imageParam.Instantiate<ImageParamControl>();
                    if (t is ImageParamControl ip)
                        ip.IconLibrary = IconLibrary;
                    break;

                case TemplateParameter.TemplateParameterType.TrackType:
                    t = _trackTypeParam.Instantiate<PopupParamControl>();
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (t is IParamControl pc)
            {
                pc.SetParameter(p);
                pc.ParameterUpdated += OnTextureUpdate;
            }

            _paramContainer.AddChild(t);
        }
    }

    private void OnTextureUpdate(object sender, EventArgs e)
    {
        SetHasUnsavedChanges();
        UpdateTexture(true, false);
    }

    private void UpdateTexture(bool updateBounds, bool updateTree)
    {
        if (updateTree)
            MapTreeToElements();

        var td = TemplateEngine.GenerateTextureDefinition(_hierarchicalElements, _textureContext);

        TextureFactory.GenerateTexture(td, UpdatePreview);

        if (updateBounds)
            UpdateBoundsRect();
    }

    private void ClearParameters()
    {
        foreach (var p in _paramContainer.GetChildren())
        {
            if (p is IParamControl pc)
                pc.ParameterUpdated -= OnTextureUpdate;
            p.QueueFree();
        }
    }

    private void UpdateBoundsRect()
    {
        if (_selectedElement == null)
        {
            _boundsRect.Hide();
            return;
        }

        _boundsRect.Show();

        var d = _selectedElement.GetElementData(_textureContext);

        if (!d.Any())
            return;

        //var te = d[0];

        //Vector2I p = new Vector2I(te.CenterX, te.CenterY);

        //var br = new Rect2I(p, te.Width, te.Height);
        Rect2I br;

        if (d.Count == 1)
        {
            var te = d[0];
            var p = new Vector2I(te.CenterX, te.CenterY);
            br = new Rect2I(p, te.Width, te.Height);
        }
        else
        {
            br = CalcOuterBoundsRect(d);
        }

        _boundsRect.SetBounds(br, _textureContext);
    }

    private Rect2I CalcOuterBoundsRect(IEnumerable<TextureFactory.TextureObject> rects)
    {
        bool first = true;

        float minX = 0,
            minY = 0;
        float maxX = 0,
            maxY = 0;

        foreach (var r in rects)
        {
            float halfW = r.Width * 0.5f;
            float halfH = r.Height * 0.5f;

            float left = r.CenterX - halfW;
            float right = r.CenterX + halfW;
            float top = r.CenterY - halfH;
            float bottom = r.CenterY + halfH;

            if (first)
            {
                minX = left;
                minY = top;
                maxX = right;
                maxY = bottom;
                first = false;
            }
            else
            {
                minX = MathF.Min(minX, left);
                minY = MathF.Min(minY, top);
                maxX = MathF.Max(maxX, right);
                maxY = MathF.Max(maxY, bottom);
            }
        }

        // Convert back to center + size
        Vector2I size = new Vector2I((int)(maxX - minX), (int)(maxY - minY));
        Vector2I center = new Vector2I((int)(minX + size.X * 0.5f), (int)(minY + size.Y * 0.5f));

        return new Rect2I(center, size);
    }

    private void AddText()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Text);
        UpdateTexture(true, true);
    }

    private void AddImage()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Image);
        UpdateTexture(true, true);
    }

    private void UpdatePreview(ImageTexture texture)
    {
        _preview.Texture = texture;
    }

    public TextureFactory TextureFactory { get; set; }

    #region Sizes

    private Dictionary<string, (float, float)> _standardSizes;

    private void InitializeStandardSizes()
    {
        _standardSizes = new();
        _standardSizes.Add("Poker", (2.5f, 3.5f));
        _standardSizes.Add("Bridge", (2.25f, 3.5f));
        _standardSizes.Add("Mini Euro", (1.75f, 2.5f));
        _standardSizes.Add("Tarot", (2.75f, 4.75f));
        _standardSizes.Add("Custom", (0, 0));

        _cardSizes.Clear();
        foreach (var kv in _standardSizes)
        {
            _cardSizes.AddItem(kv.Key);
        }

        //die standard sizes have the die size as width and 0 as height.

        _standardSizes.Add("D4", (4, 0));
        _standardSizes.Add("D6", (6, 0));
        _standardSizes.Add("D8", (8, 0));
        _standardSizes.Add("D10", (10, 0));
        _standardSizes.Add("D12", (12, 0));
        _standardSizes.Add("D20", (20, 0));

        _cardSizes.AddSeparator();
        _cardSizes.AddItem("D4");
        _cardSizes.AddItem("D6");
        _cardSizes.AddItem("D8");
        _cardSizes.AddItem("D10");
        _cardSizes.AddItem("D12");
        _cardSizes.AddItem("D20");
    }

    private float _curHeight;
    private float _curWidth;
    private string _curSizeType;

    private void OnStandardSizeChanged(long index)
    {
        if (CurrentTemplate == null)
            return;

        _curSizeType = _cardSizes.Text;

        if (!_standardSizes.TryGetValue(_curSizeType, out var size))
            return;

        _curWidth = size.Item1;
        _curHeight = size.Item2;
        EditCurrentTemplate(t => t with { SizeTemplate = _curSizeType });

        if (_curWidth == 0 && _curHeight == 0)
        {
            ShowCardMode();
            _heightInput.Text = (CurrentTemplate.Height).ToString("f1");
            _widthInput.Text = (CurrentTemplate.Width).ToString("f1");
            _curWidth = CurrentTemplate.Width;
            _curHeight = CurrentTemplate.Height;
            HeightWidthChange(string.Empty);
            return;
            //both = 0 means custom size.
        }

        if (_curHeight == 0)
        {
            //die mode
            ShowDieMode(_curWidth);
            return;
        }

        ShowCardMode();

        float conversion = 25.4f;

        _heightInput.Text = (_curHeight * conversion).ToString("f1");
        _widthInput.Text = (_curWidth * conversion).ToString("f1");

        HeightWidthChange(string.Empty);
    }

    private void ShowDieMode(float index)
    {
        _sizeControls.Visible = false;
        InitOverlay((int)index);
        _overlayControls.Visible = true;
        OnOverlayToggle();
    }

    private void ShowCardMode()
    {
        HideOverlay();
        _sizeControls.Visible = true;
        _overlayControls.Visible = false;
    }

    private void HideOverlay()
    {
        _previewOverlay.Visible = false;
    }

    private void OnOverlayToggle()
    {
        _previewOverlay.Visible = _showOverlayButton.ButtonPressed;
    }

    private void InitOverlay(int sides)
    {
        var h = 256;
        var w = 256;

        Texture2D t;

        switch (sides)
        {
            case 4:
                t = _d4_overlay;
                break;
            case 6:
                h = 170;
                t = _d6_overlay;
                break;
            case 8:
                t = _d8_overlay;
                break;
            case 10:
                t = _d10_overlay;
                break;
            case 12:
                t = _d12_overlay;
                break;
            case 20:
                t = _d20_overlay;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(sides), sides, null);
        }

        _curWidth = w;
        _curHeight = h;

        InitializeFit(w, h);

        var image = t.GetImage();
        var s = _preview.GetSize();
        image.Resize((int)s.X, (int)s.Y);
        var t2 = ImageTexture.CreateFromImage(image);
        _previewOverlay.Texture = t2;
    }

    #endregion

    #region New Template Dialog

    private MarginContainer _newTemplateDialog;
    private Button _newTemplateOk;
    private Button _newtemplateCancel;
    private LineEdit _newTemplateName;
    private LineEdit _newTemplateWidth;
    private LineEdit _newTemplateHeight;
    private OptionButton _newTemplateSize;

    private void InitializeNewTemplateDialog()
    {
        _newTemplateDialog = GetNode<MarginContainer>("%NewTemplateDialog");
        _newTemplateOk = GetNode<Button>("%NTOK");
        _newTemplateOk.Pressed += OnNewTemplateOkPressed;

        _newtemplateCancel = GetNode<Button>("%NTCancel");
        _newtemplateCancel.Pressed += _newTemplateDialog.Hide;

        _newTemplateName = GetNode<LineEdit>("%NTName");
        _newTemplateName.TextChanged += _ => UpdateNewTemplateOkButton();

        _newTemplateWidth = GetNode<LineEdit>("%NTWidth");
        _newTemplateWidth.TextChanged += _ => UpdateNewTemplateOkButton();

        _newTemplateHeight = GetNode<LineEdit>("%NTHeight");
        _newTemplateHeight.TextChanged += _ => UpdateNewTemplateOkButton();

        _newTemplateSize = GetNode<OptionButton>("%NTSize");
        _newTemplateSize.ItemSelected += NewTemplateStandardSizeChanged;
        NewTemplateStandardSizeChanged(0);

        _newTemplateSize.Clear();
        foreach (var kv in _standardSizes)
        {
            _newTemplateSize.AddItem(kv.Key);
        }
    }

    private void UpdateNewTemplateOkButton()
    {
        float.TryParse(_newTemplateWidth.Text, out var w);
        float.TryParse(_newTemplateHeight.Text, out var h);

        _newTemplateOk.Disabled =
            string.IsNullOrWhiteSpace(_newTemplateName.Text) || h <= 0 || w <= 0;
    }

    private void OnNewTemplateOkPressed()
    {
        float.TryParse(_newTemplateWidth.Text, out var w);
        float.TryParse(_newTemplateHeight.Text, out var h);

        var t = new Template
        {
            Id = Snowport.Clock.CreateTag(),
            Name = _newTemplateName.Text,
            SizeTemplate = _newTemplateSize.Text,
            Width = w,
            Height = h,
        };

        ProjectService.Instance.Upsert(t);
        CurrentTemplate = t;

        _newTemplateName.Clear();

        _newTemplateDialog.Hide();
    }

    #endregion

    #region Template management

    private void LoadTemplateNameSelector(IRecordReader R)
    {
        _templateNameSelector.Clear();

        foreach (var t in R.Get<Template>().OrderBy(v => v.Name))
        {
            _templateNameSelector.AddItem(t.Name, t.Id.Value);
        }
    }

    private void NewTemplateStandardSizeChanged(long index)
    {
        var cardType = _newTemplateSize.Text;

        if (!_standardSizes.TryGetValue(cardType, out var size))
            return;

        var w = size.Item1;
        var h = size.Item2;

        if (w == 0 || h == 0)
            return;

        float conversion = 25.4f;

        _newTemplateHeight.Text = (h * conversion).ToString("f1");
        _newTemplateWidth.Text = (w * conversion).ToString("f1");

        UpdateNewTemplateOkButton();

        HeightWidthChange(string.Empty);
    }

    private void UpdateProject()
    {
        LoadTemplateNameSelector(ProjectService.Instance);

        if (_templateNameSelector.ItemCount == 0)
        {
            CurrentTemplate = null;
            return;
        }

        _templateNameSelector.Select(0);
        CurrentTemplate = ProjectService.Instance.Get<Template>(_templateNameSelector.GetItemId(0));

        MapDataset();
    }

    #endregion

    #region DragAndDrop

    private void EnableTreeDragAndDrop()
    {
        _elementTree.SetDragForwarding(
            Callable.From<Vector2, Variant>(_GetTreeDragData),
            Callable.From<Vector2, Variant, bool>(_CanDropTreeData),
            Callable.From<Vector2, Variant>(_DropTreeData)
        );
    }

    private Variant _GetTreeDragData(Vector2 position)
    {
        var selectedItem = _elementTree.GetSelected();
        if (selectedItem == null || selectedItem == _rootItem)
            return default;

        var previewLabel = new Label();
        previewLabel.Text = selectedItem.GetText(0);
        _elementTree.SetDragPreview(previewLabel);

        var dragData = new Godot.Collections.Dictionary
        {
            { "type", "tree_item" },
            { "item_id", selectedItem.GetMetadata(0) },
            { "item_text", selectedItem.GetText(0) },
        };

        _elementTree.DropModeFlags = (int)Tree.DropModeFlagsEnum.Inbetween;

        return dragData;
    }

    private bool _CanDropTreeData(Vector2 position, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
            return false;

        var dragData = data.AsGodotDictionary();
        if (!dragData.ContainsKey("type") || dragData["type"].AsString() != "tree_item")
            return false;

        var dropSection = _elementTree.GetDropSectionAtPosition(position);
        var targetItem = _elementTree.GetItemAtPosition(position);

        if (targetItem == null)
            return false;

        if (targetItem == _rootItem)
            return true;

        var draggedId = dragData["item_id"].AsInt32();
        var draggedItem = FindTreeItemById(_rootItem, draggedId);

        if (draggedItem == null || draggedItem == targetItem)
            return false;

        if (IsItemDescendantOf(targetItem, draggedItem))
            return false;

        return true;
    }

    private void _DropTreeData(Vector2 position, Variant data)
    {
        var dragData = data.AsGodotDictionary();
        var draggedId = dragData["item_id"].AsInt32();
        var draggedItem = FindTreeItemById(_rootItem, draggedId);

        _elementTree.DropModeFlags = (int)Tree.DropModeFlagsEnum.Disabled;

        if (draggedItem == null)
            return;

        var targetItem = _elementTree.GetItemAtPosition(position);
        if (targetItem == null)
            return;

        var dropSection = _elementTree.GetDropSectionAtPosition(position);

        var draggedElement = _templateElements.FirstOrDefault(x => x.Id.Value == draggedId);
        if (draggedElement == null)
            return;

        TreeItem newParent;
        TreeItem insertBefore = null;

        if (dropSection == 0)
        {
            if (targetItem == _rootItem)
            {
                newParent = _rootItem;
            }
            else
            {
                newParent = targetItem;
            }
        }
        else if (dropSection == -1)
        {
            newParent = targetItem.GetParent();
            insertBefore = targetItem;
        }
        else
        {
            newParent = targetItem.GetParent();
            insertBefore = targetItem.GetNext();
        }

        if (newParent == null)
            newParent = _rootItem;

        var oldParent = draggedItem.GetParent();

        var newItem = _elementTree.CreateItem(newParent, GetChildIndex(newParent, insertBefore)); //newParent.GetChildIndex(insertBefore));
        newItem.SetMetadata(0, draggedElement.Id.Value);
        newItem.SetText(0, draggedElement.ElementName);

        var children = new List<TreeItem>();
        var child = draggedItem.GetFirstChild();
        while (child != null)
        {
            children.Add(child);
            child = child.GetNext();
        }

        foreach (var childItem in children)
        {
            MoveTreeItemRecursive(childItem, newItem);
        }

        oldParent?.RemoveChild(draggedItem);
        draggedItem.Free();

        _elementTree.SetSelected(newItem, 0);

        MapTreeToElements();
        SetHasUnsavedChanges();
        _updateRequired = true;
    }

    private int GetChildIndex(TreeItem parent, TreeItem child)
    {
        if (parent == null || child == null)
            return -1;

        var index = 0;
        var current = parent.GetFirstChild();
        while (current != null)
        {
            if (current == child)
                return index;
            index++;
            current = current.GetNext();
        }

        return index;
    }

    private void MoveTreeItemRecursive(TreeItem source, TreeItem newParent)
    {
        var id = source.GetMetadata(0).AsInt32();
        var text = source.GetText(0);

        var newItem = _elementTree.CreateItem(newParent);
        newItem.SetMetadata(0, id);
        newItem.SetText(0, text);

        var children = new List<TreeItem>();
        var child = source.GetFirstChild();
        while (child != null)
        {
            children.Add(child);
            child = child.GetNext();
        }

        foreach (var childItem in children)
        {
            MoveTreeItemRecursive(childItem, newItem);
        }
    }

    private TreeItem FindTreeItemById(TreeItem root, int id)
    {
        if (root == null)
            return null;

        if (root != _rootItem && root.GetMetadata(0).AsInt32() == id)
            return root;

        var child = root.GetFirstChild();
        while (child != null)
        {
            var found = FindTreeItemById(child, id);
            if (found != null)
                return found;
            child = child.GetNext();
        }

        return null;
    }

    private bool IsItemDescendantOf(TreeItem potentialDescendant, TreeItem potentialAncestor)
    {
        var current = potentialDescendant.GetParent();
        while (current != null)
        {
            if (current == potentialAncestor)
                return true;
            current = current.GetParent();
        }

        return false;
    }

    #endregion

    #region Zoom

    private Vector2 _windowSize = Vector2.Zero;
    private Vector2 _topLeftMargin = Vector2.Zero;
    private Button _zoomInButton;
    private Button _zoomOutButton;
    private Button _zoomFitButton;

    private float _zoomDelta = 0.1f;

    private float _curZoomScale = 1;

    private void Zoom(float newScale)
    {
        if (newScale < 1f)
            newScale = 1;

        _curZoomScale = newScale;

        //check to see if we have saved the _topLeftMargin. If not, cache it
        if (_topLeftMargin == Vector2.Zero)
        {
            _topLeftMargin = _preview.Position;
        }

        _preview.SetScale(new Vector2(_curZoomScale, _curZoomScale));

        UpdateScrollBarVisibility();
        OnScroll(0);
    }

    private void ZoomIn()
    {
        Zoom(_curZoomScale + _zoomDelta);
    }

    private void ZoomOut()
    {
        Zoom(_curZoomScale - _zoomDelta);
    }

    private void ZoomFit()
    {
        Zoom(1);
    }

    private void UpdateScrollBarVisibility()
    {
        _previewHScroll.Visible =
            _preview.Position.X + (_preview.Size.X * _preview.Scale.X) > _previewWindow.Size.X;

        _previewVScroll.Visible =
            _preview.Position.Y + (_preview.Size.Y * _preview.Scale.Y) > _previewWindow.Size.Y;

        //_previewHScroll.Page = 100 * _previewWindow.Size.X / (_preview.Size.X * _preview.Scale.X + _preview.Position.X);
        //_previewVScroll.Page = 100 * _previewWindow.Size.Y / (_preview.Size.Y * _preview.Scale.Y + _preview.Position.Y);

        _previewVScroll.Page = 10;
    }

    private void OnScroll(double value)
    {
        var py = _preview.Size.Y * _preview.Scale.Y;
        var px = _preview.Size.X * _preview.Scale.X;

        var _windowSize = _previewWindow.Size;

        float vv = (float)(_previewVScroll.Value / (100 - _previewVScroll.Page));

        var v0 = _topLeftMargin.Y;
        var v1 = _windowSize.Y - (_topLeftMargin.Y + py);

        float ny = (float)(Lerp(v0, v1, vv));

        float hh = (float)(_previewHScroll.Value / (100 - _previewHScroll.Page));

        var h0 = _topLeftMargin.X;
        var h1 = _windowSize.X - (_topLeftMargin.X + px);

        float nx = (float)(Lerp(h0, h1, hh));

        _preview.Position = new Vector2(nx, ny);
    }

    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private const float MarginScale = 0.9f;

    private float _previewDpi;

    private void InitializeCardFit()
    {
        float.TryParse(_widthInput.Text, out var w);
        float.TryParse(_heightInput.Text, out var h);
        InitializeFit(w, h);
    }

    //It may take a few frames for the Size of the preview window to be set properly
    private bool _fitRequired;

    public void InitializeFit(float w, float h)
    {
        if (w <= 0 || h <= 0)
            return;

        if (_previewWindow.Size == Vector2.Zero)
        {
            _fitRequired = true;
            return;
        }

        _fitRequired = false;

        //size to fit in the preview window
        var aspectRation = w / h;

        var wSize = _previewWindow.Size;

        var sh = wSize.Y * MarginScale;

        var sw = sh * aspectRation;

        if (sw > wSize.X * MarginScale)
        {
            sw = wSize.X * MarginScale;
            sh = sw / aspectRation;
        }

        //scale / position the preview window
        _preview.SetSize(new Vector2(sw, sh));
        _preview.SetPosition(new Vector2((wSize.X - sw) / 2, (wSize.Y - sh) / 2));

        //_previewOverlay.SetSize(new Vector2(sw, sh));
        //_previewOverlay.SetPosition(new Vector2((wSize.X - sw) / 2, (wSize.Y - sh) / 2));

        _previewDpi = 25.4f * sw / w;

        _textureContext.ParentSize = new Vector2(sw, sh);
        _textureContext.Dpi = 25.4f * _textureContext.ParentSize.Y / h;

        ZoomFit();
    }

    #endregion

    #region Datasets

    private void ChangePage(object sender, ItemSelectedEventArgs e)
    {
        var rows = ProjectService.Instance.GetRows(CurrentTemplate.DataSet);
        _textureContext.CurrentRow = e.Index >= 0 && e.Index < rows.Count ? rows[e.Index] : null;
        _updateRequired = true;
    }

    //Different dataset has been selected by the user
    private void OnDatasetChanged(SnowTag datasetRef)
    {
        if (datasetRef == SnowTag.Empty)
        {
            _textureContext.DataSet = null;
            _textureContext.CurrentRow = null;
            _pageControl.Hide();
            EditCurrentTemplate(t => t with { DataSet = SnowTag.Empty });
        }
        else
        {
            EditCurrentTemplate(t => t with { DataSet = datasetRef });
        }

        UpdateTextureContext(CurrentTemplate.DataSet);

        _updateRequired = true;
    }

    private void UpdateTextureContext(SnowTag datasetRef)
    {
        var dataset = ProjectService.Instance.Get<DataSet>(datasetRef);
        if (dataset != null)
        {
            EditCurrentTemplate(t => t with { DataSet = datasetRef });
            _textureContext.DataSet = dataset;
            var rows = ProjectService.Instance.GetRows(datasetRef);
            _textureContext.CurrentRow = rows.Count > 0 ? rows[0] : null;
        }
    }

    private void MapDataset()
    {
        var datasetRef = CurrentTemplate.DataSet;

        if (datasetRef == SnowTag.Empty || ProjectService.Instance.Get<DataSet>(datasetRef) == null)
        {
            _textureContext.DataSet = null;
            _textureContext.CurrentRow = null;
            _dataSetSelector.SelectedDataSet = SnowTag.Empty;
            _pageControl.Hide();
            _updateRequired = true;
            return;
        }

        _dataSetSelector.SelectedDataSet = datasetRef;

        UpdateTextureContext(datasetRef);

        _pageControl.SetItemCount(ProjectService.Instance.GetRows(datasetRef).Count);
        _pageControl.Show();

        _updateRequired = true;
    }

    #endregion
}

public class TextureContext()
{
    public Vector2 ParentSize { get; set; }
    public float Dpi { get; set; }

    public DataSet DataSet { get; set; }

    public DataRow CurrentRow { get; set; }
}

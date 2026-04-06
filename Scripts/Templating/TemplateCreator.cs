using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Lizzie.Scripts.Templating;

public partial class TemplateCreator : Window
{
    [Export]
    private TextureRect _preview;

    //private VBoxContainer _elementContainer;

    private Tree _elementTree;
    private VBoxContainer _paramContainer;

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
    private OptionButton _dataSetSelector;

    private PageControl _pageControl;

    private AcceptDialog _acceptDialog;
    private ConfirmationDialog _saveBeforeCloseDialog;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        InitPreview();

        InitToolbar();

        InitElementTree();

        InitParamTypes();

        InitializeNewTemplateDialog();

        _textureContext.ParentSize = _preview.GetSize();

        InitializeFit();

        _acceptDialog = (AcceptDialog)GetNode("ConfirmationDialog");

        InitializeSaveBeforeCloseDialog();

        this.VisibilityChanged += UpdateScrollBarVisibility;
        this.CloseRequested += OnCloseRequested;

        UpdateProject();

        if (!string.IsNullOrWhiteSpace(_tempTemplateName))
        {
            SetTemplateByName(_tempTemplateName);
        }
    }

    private void InitToolbar()
    {
        _templateNameSelector = GetNode<OptionButton>("%TemplateName");
        LoadTemplateNameSelector();
        _templateNameSelector.ItemSelected += ChangeTemplate;

        _newButton = GetNode<Button>("%NewButton");
        _newButton.Pressed += () => _newTemplateDialog.Show();

        _saveButton = GetNode<Button>("%SaveButton");
        _saveButton.Pressed += SaveTemplate;

        _duplicateButton = GetNode<Button>("%DuplicateButton");

        _closeButton = GetNode<Button>("%CloseButton");
        _closeButton.Pressed += OnClose;

        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += HeightWidthChange;

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += HeightWidthChange;

        _cardSizes = GetNode<OptionButton>("%StandardSize");
        _cardSizes.ItemSelected += StandardSizeChanged;
        InitializeStandardSizes();
        StandardSizeChanged(0);

        _dataSetSelector = GetNode<OptionButton>("%Dataset");
        _dataSetSelector.ItemSelected += OnDatasetChanged;
        InitializeDataSets();

        _pageControl = GetNode<PageControl>("%PageControl");
        _pageControl.Hide();
        _pageControl.ItemSelected += ChangePage;
    }

    private void OnClose()
    {
        // Show save confirmation dialog
        _saveBeforeCloseDialog.PopupCentered();
    }

    private void OnCloseRequested()
    {
        // When user clicks X button, show save confirmation instead of closing immediately
        _saveBeforeCloseDialog.PopupCentered();
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

    public Template CurrentTemplate
    {
        get => _currentTemplate;
        set
        {
            _currentTemplate = value;
            MapTemplate();
        }
    }

    private string _tempTemplateName = string.Empty;

    public void SetTemplateByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!IsNodeReady())
        {
            _tempTemplateName = name;
            return;
        }

        if (Templates.TryGetValue(name, out var template))
        {
            for (int i = 0; i < _templateNameSelector.ItemCount; i++)
            {
                var n = _templateNameSelector.GetItemText(i);
                if (n == name)
                {
                    _templateNameSelector.Selected = i;
                    ChangeTemplate(i);
                }
            }
        }
    }

    private void MapTemplate()
    {
        //change sizes

        //set up element tree
        _elementTree.Clear();
        _rootItem = _elementTree.CreateItem(); //create root item

        _templateElements.Clear();

        ClearParameterBox();

        int id = 0;
        foreach (var t in CurrentTemplate.Elements)
        {
            var te = TemplateEngine.BuildTemplateElement(t);

            var ni = _elementTree.CreateItem(_rootItem);

            if (te.Id == 0)
                te.Id = id;
            ni.SetMetadata(0, te.Id);
            id++;
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
        string name = _templateNameSelector.GetItemText((int)index);
        UpdateTemplate(CurrentTemplate);

        if (Templates.ContainsKey(name))
        {
            SaveTemplate(); //save current template before switching
            CurrentTemplate = Templates[name];
        }
    }

    private void SaveTemplate()
    {
        CurrentTemplate.Elements = TemplateEngine.MapTemplateElementsToProjectFormat(
            _hierarchicalElements
        );
        ProjectService.Instance.SaveProject(ProjectService.Instance.CurrentProject);
        EventBus.Instance.Publish(
            new TemplateChangedEvent
            {
                TemplateName = _currentTemplate.Name,
                Template = _currentTemplate,
            }
        );

        EventBus.Instance.Publish<ProjectChangedEvent>();
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
        InitializeFit();
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

        var m = _boundsRect.GetBounds();

        int w = (int)_textureContext.ParentSize.X - m.l - m.r;
        int h = (int)_textureContext.ParentSize.Y - m.t - m.b;

        UpdateParamControl("X", (m.l + w / 2).ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Y", (m.t + h / 2).ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Width", w.ToString(CultureInfo.InvariantCulture));
        UpdateParamControl("Height", h.ToString(CultureInfo.InvariantCulture));

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
        var p = _templateElements.FirstOrDefault(x => x.Id == id);
        return p;
    }

    #region Element Tools

    private void DeleteCurrentElementQuery()
    {
        if (_selectedElement == null)
            return;
        var ti = _elementTree.GetSelected();

        if (ti.GetMetadata(0).AsInt32() != _selectedElement.Id)
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
        _updateRequired = true;
    }

    private void RenameCurrentElement()
    {
        if (_selectedElement == null)
            return;
        var ti = _elementTree.GetSelected();
        ti.SetEditable(0, true);
    }

    private void DuplicateCurrentElement()
    {
        if (_selectedElement == null)
            return;

        var max = GetMaxId(_rootItem) + 1;

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
            case ITemplateElement.TemplateElementType.Cirlce:
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

        var elementName = $"{_selectedElement.ElementName}{max}";

        var ni = _elementTree.CreateItem(_rootItem); //update so that it places item as sibling to selected
        ni.SetMetadata(0, max);
        ni.SetText(0, elementName);

        t.Id = max;
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
        var max = GetMaxId(_rootItem) + 1;

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
            case ITemplateElement.TemplateElementType.Cirlce:
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

        var elementName = $"{prefix}{max}";

        var ni = _elementTree.CreateItem(_rootItem);
        ni.SetMetadata(0, max);
        ni.SetText(0, elementName);

        t.Id = max;
        t.ElementName = elementName;
        _templateElements.Add(t);

        _elementTree.SetSelected(ni, 0);

        MapTreeToElements();
    }

    /// <summary>
    /// Recursively iterates through all TreeItems starting from a given item.
    /// </summary>
    private int GetMaxId(TreeItem item)
    {
        int maxId = 0;

        if (item == null)
            return 0;

        var id = item.GetMetadata(0).AsInt32();
        maxId = Math.Max(maxId, id);

        // Iterate over children
        TreeItem child = item.GetFirstChild();
        while (child != null)
        {
            maxId = Math.Max(GetMaxId(child), maxId); // Recursive call
            child = child.GetNext();
        }

        return maxId;
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
    }

    private float _curHeight;
    private float _curWidth;
    private string _curSizeType;

    private void StandardSizeChanged(long index)
    {
        _curSizeType = _cardSizes.Text;

        if (!_standardSizes.TryGetValue(_curSizeType, out var size))
            return;

        _curWidth = size.Item1;
        _curHeight = size.Item2;

        if (_curWidth == 0 || _curHeight == 0)
            return;

        float conversion = 25.4f;

        _heightInput.Text = (_curHeight * conversion).ToString("f1");
        _widthInput.Text = (_curWidth * conversion).ToString("f1");

        HeightWidthChange(string.Empty);
    }

    private void InitializeDataSets()
    {
        _dataSetSelector.Clear();
        _dataSetSelector.AddItem("(none)", 0);

        var i = 1;

        foreach (var d in ProjectService.Instance.CurrentProject.Datasets)
        {
            _dataSetSelector.AddItem(d.Key, i);
            i++;
        }

        _dataSetSelector.Select(0);
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
            string.IsNullOrWhiteSpace(_newTemplateName.Text)
            || Templates.ContainsKey(_newTemplateName.Text)
            || h <= 0
            || w <= 0;
    }

    private void OnNewTemplateOkPressed()
    {
        //save current template
        UpdateTemplate(CurrentTemplate);

        var t = new Template { Name = _newTemplateName.Text, SizeTemplate = _newTemplateSize.Text };

        float.TryParse(_newTemplateWidth.Text, out var w);
        float.TryParse(_newTemplateHeight.Text, out var h);

        t.Width = w;
        t.Height = h;

        Templates.Add(t.Name, t);
        _templateNameSelector.AddItem(t.Name);
        _templateNameSelector.Select(_templateNameSelector.GetItemCount() - 1);

        CurrentTemplate = t;

        _newTemplateName.Clear();

        _newTemplateDialog.Hide();
    }

    #endregion

    #region Template management

    private Dictionary<string, Template> _templates = new();

    public Dictionary<string, Template> Templates
    {
        get
        {
            var p = ProjectService.Instance.CurrentProject;
            if (p == null)
                return new();
            return p.Templates;
        }
    }

    private void LoadTemplateNameSelector()
    {
        if (_templateNameSelector == null)
            return;

        _templateNameSelector.Clear();

        foreach (var kv in Templates.OrderBy(x => x.Key))
        {
            _templateNameSelector.AddItem(kv.Key);
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

        HeightWidthChange(string.Empty);
    }

    /*
    private TemplateElement BuildTemplateElement(Dictionary<string, string> parameters)
    {
        TemplateElement te;

        if (!parameters.TryGetValue("Type", out var type)) return null;

        switch (type)
        {
            case "Text":
                te = new TextElement();
                break;
            case "Image":
                te = new ImageElement();
                break;

            default:
                return null;
        }

        te.ElementName = parameters.TryGetValue("Name", out var name) ? name : string.Empty;
        te.Id = parameters.TryGetValue("Id", out var id) ? int.Parse(id) : 0;


        foreach (var kv in parameters)
        {
            te.SetParameterValue(kv.Key, kv.Value);
        }

        return te;
    }
    */

    private Dictionary<string, string> ExportTemplateElement(ITemplateElement te)
    {
        var parameters = new Dictionary<string, string>();

        parameters.Add("Name", te.ElementName);

        foreach (var p in te.Parameters)
        {
            parameters.Add(p.Name, p.Value);
        }

        var tp = "Text";
        if (te is ImageElement)
            tp = "Image";

        parameters.Add("Type", tp);

        return parameters;
    }

    private void UpdateTemplate(Template template)
    {
        template.SizeTemplate = _curSizeType;
        template.Width = _curWidth;
        template.Height = _curHeight;

        template.Elements.Clear();
        foreach (var e in _templateElements)
        {
            template.Elements.Add(ExportTemplateElement(e));
        }
    }

    private void UpdateProject()
    {
        _templateNameSelector.Clear();
        foreach (var kv in ProjectService.Instance.CurrentProject.Templates)
        {
            _templateNameSelector.AddItem(kv.Key);
        }

        _templateNameSelector.Select(0);
        CurrentTemplate = ProjectService.Instance.CurrentProject.Templates[
            _templateNameSelector.GetItemText(0)
        ];

        InitializeDataSets();
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

        var draggedElement = _templateElements.FirstOrDefault(x => x.Id == draggedId);
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
        newItem.SetMetadata(0, draggedElement.Id);
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

    public void InitializeFit()
    {
        float.TryParse(_widthInput.Text, out var w);
        float.TryParse(_heightInput.Text, out var h);

        if (w <= 0 || h <= 0)
            return;

        _textureContext.ParentSize = _preview.Size;
        _textureContext.Dpi = 25.4f * _textureContext.ParentSize.Y / h;

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

        _previewDpi = 25.4f * sw / w;

        ZoomFit();
    }

    #endregion

    #region Datasets

    private void ChangePage(object sender, ItemSelectedEventArgs e)
    {
        _textureContext.CurrentRowName = e.Caption;
        _updateRequired = true;
    }

    //Different dataset has been selected by the user
    private void OnDatasetChanged(long index)
    {
        if (index == 0)
        {
            _textureContext.DataSet = null;
            _textureContext.CurrentRowName = string.Empty;
            _pageControl.Hide();
            CurrentTemplate.DataSet = string.Empty;
        }
        else
        {
            var n = _dataSetSelector.GetItemText((int)index);
        }

        UpdateTextureContext(CurrentTemplate.DataSet);

        _updateRequired = true;
    }

    private void UpdateTextureContext(string datasetName)
    {
        if (ProjectService.Instance.CurrentProject.Datasets.ContainsKey(datasetName))
        {
            CurrentTemplate.DataSet = datasetName;
            _textureContext.DataSet = ProjectService.Instance.CurrentProject.Datasets[datasetName];
            _textureContext.CurrentRowName = _textureContext.DataSet.Rows.First().Key;
        }
    }

    private void MapDataset()
    {
        int index = 0;

        for (var i = 0; i < _dataSetSelector.GetItemCount(); i++)
        {
            if (_dataSetSelector.GetItemText(i) == CurrentTemplate.DataSet)
            {
                index = i;
                break;
            }
        }

        _dataSetSelector.Select(index);

        UpdateTextureContext(CurrentTemplate.DataSet);

        _pageControl.SetItemLabels(_textureContext.DataSet.Rows.Select(x => x.Key).ToArray());
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

    public string CurrentRowName { get; set; }
}

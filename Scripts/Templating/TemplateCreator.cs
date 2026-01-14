using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using TTSS.Scripts.Templating;

public partial class TemplateCreator : MarginContainer
{
    [Export] private TextureRect _preview;

    //private VBoxContainer _elementContainer;

    private Tree _elementTree;
    private VBoxContainer _paramContainer;

    private Button _textButton;
    private Button _closeButton;
    private Button _imageButton;

    private BoundsRect _boundsRect;

    private PackedScene _stringParam;
    private PackedScene _numberParam;
    private PackedScene _colorParam;
    private PackedScene _anchorParam;
    private PackedScene _boolParam;
    private PackedScene _horJustifyParam;
    private PackedScene _verJustifyParam;
    private PackedScene _imageParam;

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
    public Button _saveButton;
    public Button _duplicateButton;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        InitToolbar();
        
        InitElementTree();

        InitParamTypes();

        InitPreview();

        InitializeNewTemplateDialog();
        
        _textureContext.ParentSize = _preview.GetSize();
    }


    private void InitToolbar()
    {
        _templateNameSelector = GetNode<OptionButton>("%TemplateName");
        LoadTemplateNameSelector();
        _templateNameSelector.ItemSelected += ChangeTemplate;
        
        _newButton = GetNode<Button>("%NewButton");
        _newButton.Pressed += () => _newTemplateDialog.Show();
        
        _saveButton = GetNode<Button>("%SaveButton");
        
        _duplicateButton = GetNode<Button>("%DuplicateButton");
        

        _closeButton = GetNode<Button>("%CloseButton");
        _closeButton.Pressed += Hide;

        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += HeightWidthChange;

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += HeightWidthChange;

        _cardSizes = GetNode<OptionButton>("%StandardSize");
        _cardSizes.ItemSelected += StandardSizeChanged;
        InitializeStandardSizes();
        StandardSizeChanged(0);
    }
    
    private Template _currentTemplate;

    public Template CurrentTemplate
    {
        get => _currentTemplate;
        set {_currentTemplate = value;
            MapTemplate();
        }
    }

    private void MapTemplate()
    {
        //change sizes
        
        //set up element tree
        
        
        
        //update preview
    }

    private void ChangeTemplate(long index)
    {
        string name = _templateNameSelector.GetItemText((int)index);
        
        if (Templates.ContainsKey(name)) CurrentTemplate = Templates[name];
    }

    private void InitPreview()
    {
        _boundsRect = GetNode<BoundsRect>("%BoundsRect");
        _boundsRect.Hide();
        _boundsRect.BoundsChanged += BoundsChanged;

        _updateTimer = GetNode<Timer>("Timer");
        _updateTimer.Timeout += UpdateTimerExpired;
        _updateTimer.Start();
    }

    private void InitElementTree()
    {
        _elementTree = GetNode<Tree>("%TemplateTree");
        _paramContainer = GetNode<VBoxContainer>("%TemplateParams");
        _textButton = GetNode<Button>("%TextButton");
        _textButton.Pressed += AddText;

        _imageButton = GetNode<Button>("%ImageButton");
        _imageButton.Pressed += AddImage;

        _rootItem = _elementTree.CreateItem(); //create root item
        _elementTree.ItemSelected += TreeItemSelected;
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
    }

    private void HeightWidthChange(string newtext)
    {
        //update preview
    }

    public IconLibrary IconLibrary { get; set; } = new();

    private void UpdateTimerExpired()
    {
        if (_updateRequired)
        {
            _updateRequired = false;
            UpdateTexture(false);
        }
    }

    private void BoundsChanged(object sender, EventArgs e)
    {
        if (_selectedElement == null) return;

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
        if (p != null) p.UpdateParameter(value);
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
        //get the Id
        var id = _elementTree.GetSelected().GetMetadata(0).AsInt32();

        //matching param
        var p = _templateElements.FirstOrDefault(x => x.Id == id);
        if (p != null)
        {
            _selectedElement = p;
            RemapParameters();
            _boundsRect.Show();
            UpdateBoundsRect();
        }
    }

    private void AddTextureElement(ITemplateElement.TemplateElementType type)
    {
        var max = GetMaxId(_rootItem) + 1;

        TemplateElement t;
        string prefix;

        if (type == ITemplateElement.TemplateElementType.Image)
        {
            t = new ImageElement();
            prefix = "Image";
        }
        else
        {
            t = new TextElement();
            prefix = "Text";
        }


        var ni = _elementTree.CreateItem(_rootItem);
        ni.SetMetadata(0, max);
        ni.SetText(0, $"{prefix} {max}");

        t.Id = max;

        _templateElements.Add(t);

        _elementTree.SetSelected(ni, 0);
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
        if (_selectedElement == null) return;

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
                    if (t is ImageParamControl ip) ip.IconLibrary = IconLibrary;
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
        UpdateTexture(true);
    }

    private void UpdateTexture(bool updateBounds)
    {
        _textureContext.ParentSize = _preview.GetSize();

        var td = new TextureFactory.TextureDefinition
        {
            BackgroundColor = Colors.White,
            Height = (int)_textureContext.ParentSize.Y,
            Width = (int)_textureContext.ParentSize.X,
            Shape = TextureFactory.TokenShape.Square
        };

        foreach (var element in _templateElements)
        {
            foreach (var l in element.GetElementData(_textureContext))
            {
                td.Objects.Add(new TextureFactory.TextureObject
                {
                    Width = l.Width,
                    Height = l.Height,
                    CenterX = l.CenterX,
                    CenterY = l.CenterY,
                    Anchor = l.Anchor,
                    Multiline = true,
                    Text = l.Text,
                    ForegroundColor = l.ForegroundColor,
                    Font = new SystemFont(),
                    FontSize = l.FontSize,
                    Autosize = l.Autosize,
                    HorizontalAlignment = l.HorizontalAlignment,
                    VerticalAlignment = l.VerticalAlignment,
                    Type = l.Type
                });
            }
        }

        TextureFactory.GenerateTexture(td, UpdatePreview);

        if (updateBounds) UpdateBoundsRect();
    }

    private void ClearParameters()
    {
        foreach (var p in _paramContainer.GetChildren())
        {
            if (p is IParamControl pc) pc.ParameterUpdated -= OnTextureUpdate;
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

        //parent bounds are always in node 0;
        if (!d.Any()) return;

        var te = d[0];

        Vector2I p = new Vector2I(te.CenterX, te.CenterY);

        var br = new Rect2I(p, te.Width, te.Height);
        _boundsRect.SetBounds(br, _textureContext);
    }


    private void AddText()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Text);
        UpdateTexture(true);
    }

    private void AddImage()
    {
        AddTextureElement(ITemplateElement.TemplateElementType.Image);
        UpdateTexture(true);
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


    private void StandardSizeChanged(long index)
    {
        var cardType = _cardSizes.Text;

        if (!_standardSizes.TryGetValue(cardType, out var size)) return;

        var w = size.Item1;
        var h = size.Item2;

        if (w == 0 || h == 0) return;

        float conversion = 25.4f;

        _heightInput.Text = (h * conversion).ToString("f1");
        _widthInput.Text = (w * conversion).ToString("f1");

        HeightWidthChange(string.Empty);
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

        _newTemplateOk.Disabled = string.IsNullOrWhiteSpace(_newTemplateName.Text) ||
                                  Templates.ContainsKey(_newTemplateName.Text) ||
                                  h <= 0 ||
                                  w <= 0;
    }

    private void OnNewTemplateOkPressed()
    {
        var t = new Template
        {
            Name = _newTemplateName.Text,
            SizeTemplate = _newTemplateSize.Text,
        };

        float.TryParse(_newTemplateWidth.Text, out var w);
        float.TryParse(_newTemplateHeight.Text, out var h);

        t.Width = w;
        t.Height = h;

        Templates.Add(t.Name, t);
        _templateNameSelector.AddItem(t.Name);
        _templateNameSelector.Select(_templateNameSelector.GetItemCount() - 1);
        
        _newTemplateName.Clear();
        
        _newTemplateDialog.Hide();
    }

    #endregion

    #region Template management

    private Dictionary<string, Template> _templates = new();

    public Dictionary<string, Template> Templates
    {
        get => _templates;
        set
        {
            _templates = value;
            LoadTemplateNameSelector();
        }
    }

    private void LoadTemplateNameSelector()
    {
        if (_templateNameSelector == null) return;

        _templateNameSelector.Clear();

        foreach (var kv in Templates.OrderBy(x => x.Key))
        {
            _templateNameSelector.AddItem(kv.Key);
        }
    }

    private void NewTemplateStandardSizeChanged(long index)
    {
        var cardType = _newTemplateSize.Text;

        if (!_standardSizes.TryGetValue(cardType, out var size)) return;

        var w = size.Item1;
        var h = size.Item2;

        if (w == 0 || h == 0) return;

        float conversion = 25.4f;

        _newTemplateHeight.Text = (h * conversion).ToString("f1");
        _newTemplateWidth.Text = (w * conversion).ToString("f1");

        HeightWidthChange(string.Empty);
    }

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

        foreach (var kv in parameters)
        {
            te.SetParameterValue(kv.Key, kv.Value);
        }
        
        return te;
    }

    private Dictionary<string, string> ExportTemplateElement(TemplateElement te)
    {
        var parameters = new Dictionary<string, string>();
        
        parameters.Add("Name", te.ElementName);
        
        foreach (var p in te.Parameters)
        {
            parameters.Add(p.Name, p.Value);
        }
        
        parameters.Add("Type", te.ElementName);
        
        return parameters;
    }
    
    
    #endregion
}

public class TextureContext()
{
    public Vector2 ParentSize { get; set; }
}
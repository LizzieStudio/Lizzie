using System;
using System.Collections.Generic;
using Godot;

public partial class PrintedPanelDialogResult : ComponentPanelDialogResult
{
    private LineEdit _nameInput;
    private LineEdit _heightInput;
    private LineEdit _widthInput;
    private LineEdit _thicknessInput;

    private OptionButton _typePicker;
    private OptionButton _shapePicker;
    private Label _orientationLabel;
    private HBoxContainer _orientationRow;
    private Button _portraitButton;
    private Button _landscapeButton;
    private Texture2D _iconPortrait;
    private Texture2D _iconLandscape;
    private Texture2D _iconHexPoint;
    private Texture2D _iconHexFlat;
    private Label _sizeLabel;
    private Label _widthTimes;

    private ComponentPreview _preview;

    private SideEditor _front;
    private SideEditor _back;
    private CheckBox _sameAsFront;

    private record ShapePreset(string Label, int Shape, float W = 0f, float H = 0f, float T = 0f);

    private static readonly Dictionary<string, ShapePreset[]> ShapesByType = new()
    {
        ["Card"] = new[]
        {
            new ShapePreset("Poker Card", 0, 63.5f, 88.9f, 0.2f),
            new ShapePreset("Bridge Card", 0, 57.15f, 88.9f, 0.2f),
            new ShapePreset("Mini Euro Card", 0, 44.45f, 63.5f, 0.2f),
            new ShapePreset("Tarot Card", 0, 69.85f, 120.65f, 0.2f),
        },
        ["Token"] = new[]
        {
            new ShapePreset("Rectangle", 0, 25.4f, 25.4f, 1f),
            new ShapePreset("Circle", 1, 25.4f, 25.4f, 1f),
            new ShapePreset("Hex", 2, 25.4f, 25.4f, 1f),
        },
        ["Board"] = new[]
        {
            new ShapePreset("Quad Fold", 0, 254f, 254f, 2f),
            new ShapePreset("Hex Fold", 0, 762f, 254f, 2f),
        },
    };

    private bool _suppressShapeReset;

    public override void _EnterTree()
    {
        RecordService.Instance.Watch(this, Sync);
    }

    private void Sync(IWatchReader R)
    {
        int count = Parameters().CardCount(R);
        _preview.ItemCount = count;
        _preview.MultiItemMode = count > 1;
        UpdatePreview();
    }

    public override void _Ready()
    {
        _nameInput = GetNode<LineEdit>("%ItemName");

        _heightInput = GetNode<LineEdit>("%Height");
        _heightInput.TextChanged += _ =>
        {
            if (!_suppressShapeReset)
                UpdateOrientationButtons();
            UpdatePreview();
        };

        _widthInput = GetNode<LineEdit>("%Width");
        _widthInput.TextChanged += _ =>
        {
            if (!_suppressShapeReset)
                UpdateOrientationButtons();
            UpdatePreview();
        };

        _thicknessInput = GetNode<LineEdit>("%Thickness");
        _thicknessInput.TextChanged += _ => UpdatePreview();

        _typePicker = GetNode<OptionButton>("%TypePicker");
        _typePicker.ItemSelected += OnTypeChanged;

        _shapePicker = GetNode<OptionButton>("%ShapePicker");
        _shapePicker.ItemSelected += OnShapeChanged;

        _orientationLabel = GetNode<Label>("%OrientationLabel");
        _orientationRow = GetNode<HBoxContainer>("%OrientationRow");
        _portraitButton = GetNode<Button>("%PortraitButton");
        _landscapeButton = GetNode<Button>("%LandscapeButton");
        _portraitButton.Pressed += OnOrientationChanged;
        _landscapeButton.Pressed += OnOrientationChanged;

        _iconPortrait = GD.Load<Texture2D>(
            "res://Textures/UI/crop_portrait_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg"
        );
        _iconLandscape = GD.Load<Texture2D>(
            "res://Textures/UI/crop_landscape_24dp_FFFFFF_FILL0_wght400_GRAD0_opsz24.svg"
        );
        _iconHexPoint = GD.Load<Texture2D>("res://Textures/UI/hex_point_up.svg");
        _iconHexFlat = GD.Load<Texture2D>("res://Textures/UI/hex_edge_up.svg");

        _sizeLabel = GetNode<Label>("%SizeLabel");
        _widthTimes = GetNode<Label>("%X");

        _front = GetNode<SideEditor>("%Front");
        _back = GetNode<SideEditor>("%Back");
        _sameAsFront = GetNode<CheckBox>("%SameAsFront");
        _front.Changed += Refresh;
        _back.Changed += Refresh;
        _sameAsFront.Toggled += same =>
        {
            _back.Visible = !same;
            Refresh();
        };

        _preview = GetNode<ComponentPreview>("%Preview");
        _preview.ItemSelected += PreviewOnItemSelected;

        OnTypeChanged(0);
    }

    /// <summary>
    /// Counts the cards again and redraws the preview, since a side changed.
    /// </summary>
    private void Refresh() => RecordService.Instance.QueueSync(this);

    private string TypeKey() =>
        _typePicker.Selected switch
        {
            0 => "Card",
            1 => "Token",
            2 => "Board",
            _ => "Card",
        };

    private ShapePreset GetCurrentShape()
    {
        var cuts = ShapesByType[TypeKey()];
        var idx = Math.Clamp(_shapePicker.Selected, 0, cuts.Length - 1);
        return cuts[idx];
    }

    private int GetEffectiveShape()
    {
        var cut = GetCurrentShape();
        if (cut.Shape == 2 && _landscapeButton.ButtonPressed)
            return 3; // Hex Edge Up
        return cut.Shape;
    }

    private void OnTypeChanged(long _)
    {
        _shapePicker.Clear();
        foreach (var cut in ShapesByType[TypeKey()])
            _shapePicker.AddItem(cut.Label);
        OnShapeChanged(0);
    }

    private void OnShapeChanged(long _)
    {
        var cut = GetCurrentShape();
        if (cut.W != 0f)
        {
            _suppressShapeReset = true;
            _widthInput.Text = cut.W.ToString("f1");
            _heightInput.Text = cut.H.ToString("f1");
            _thicknessInput.Text = cut.T.ToString("f1");
            _suppressShapeReset = false;
        }

        UpdateDimensionUI();

        if (Visible && !_suppressShapeReset)
            Activate();
    }

    private void OnOrientationChanged()
    {
        if (_suppressShapeReset)
            return;
        var cut = GetCurrentShape();
        if (cut.Shape == 0)
        {
            if (
                float.TryParse(_widthInput.Text, out var w)
                && float.TryParse(_heightInput.Text, out var h)
            )
            {
                bool wantLandscape = _landscapeButton.ButtonPressed;
                bool needsSwap = wantLandscape ? h > w : w > h;
                if (needsSwap)
                {
                    _suppressShapeReset = true;
                    (_widthInput.Text, _heightInput.Text) = (_heightInput.Text, _widthInput.Text);
                    _suppressShapeReset = false;
                }
            }
        }

        UpdatePreview();
    }

    private void UpdateOrientationButtons()
    {
        var cut = GetCurrentShape();
        if (cut.Shape != 0)
            return;
        if (
            !float.TryParse(_widthInput.Text, out var w)
            || !float.TryParse(_heightInput.Text, out var h)
        )
            return;
        _landscapeButton.ButtonPressed = w > h;
        _portraitButton.ButtonPressed = h >= w;
    }

    private void UpdateDimensionUI()
    {
        int shape = GetEffectiveShape();
        bool isRect = shape == 0;
        bool isCircle = shape == 1;
        bool isHex = shape == 2 || shape == 3;

        _widthInput.Visible = isRect;
        _widthTimes.Visible = isRect;
        _sizeLabel.Text =
            isCircle ? "Radius × D"
            : isRect ? "W × H × D"
            : "Size × D";
        _orientationLabel.Visible = !isCircle;
        _orientationRow.Visible = !isCircle;

        _portraitButton.Icon = isHex ? _iconHexPoint : _iconPortrait;
        _landscapeButton.Icon = isHex ? _iconHexFlat : _iconLandscape;

        UpdateOrientationButtons();
    }

    public override void Activate()
    {
        var comp = GetPreviewComponent();
        _preview.SetComponent(comp, new Vector3(Mathf.DegToRad(90), 0, 0));
        UpdatePreview();
    }

    private VcToken GetPreviewComponent()
    {
        int shape = GetEffectiveShape();

        string sceneName = shape switch
        {
            1 => "VcTokenCircle.tscn",
            2 => "VcTokenHexPoint.tscn",
            3 => "VcTokenHexFlat.tscn",
            _ => "VcToken.tscn",
        };

        var scene = GD.Load<PackedScene>($"res://Scenes/VisualComponents/{sceneName}");
        var vc = scene.Instantiate<VcToken>();
        return vc;
    }

    public override void Deactivate()
    {
        _preview.ClearComponent();
    }

    public override ComponentParameters GetParams()
    {
        var d = Parameters();
        // More than one card spawns as a deck.
        return d.CardCount(RecordService.Instance) > 1 ? d : d.CloneAs<TokenParameters>();
    }

    private DeckParameters Parameters()
    {
        int shape = GetEffectiveShape();

        float height = ParamToFloat(_heightInput.Text);
        float width = shape == 0 ? ParamToFloat(_widthInput.Text) : height;

        return new DeckParameters
        {
            ComponentName = _nameInput.Text,
            Height = height,
            Width = width,
            Thickness = ParamToFloat(_thicknessInput.Text),
            Shape = shape,
            Type = _typePicker.Selected switch
            {
                0 => VcToken.TokenType.Card,
                2 => VcToken.TokenType.Board,
                _ => VcToken.TokenType.Token,
            },
            Front = _front.Side,
            Back = _sameAsFront.ButtonPressed ? null : _back.Side,
        };
    }

    private void UpdatePreview()
    {
        float h = ParamToFloat(_heightInput.Text);
        float w = GetEffectiveShape() == 0 ? ParamToFloat(_widthInput.Text) : h;
        float t = ParamToFloat(_thicknessInput.Text);
        if (h == 0 || w == 0 || t == 0)
        {
            _preview.SetComponentVisibility(false);
            return;
        }

        _preview.SetComponentVisibility(true);

        var parameters = (PrintedParameters)GetParams();
        _preview.Build(
            parameters,
            parameters.Card(_preview.CurrentItem, RecordService.Instance),
            TextureFactory
        );
    }

    private void PreviewOnItemSelected(object sender, ItemSelectedEventArgs e) => UpdatePreview();

    public override void DisplayPrototype(Prototype prototype)
    {
        var p = (PrintedParameters)prototype.Parameters;
        _suppressShapeReset = true;

        var storedType = p.Type;

        int storedShape = p.Shape;

        if (storedType == VcToken.TokenType.Card)
        {
            _typePicker.Select(0);
            OnTypeChanged(0);
            _shapePicker.Select(0);
        }
        else if (storedType == VcToken.TokenType.Board)
        {
            _typePicker.Select(2);
            OnTypeChanged(2);
            _shapePicker.Select(0);
        }
        else
        {
            _typePicker.Select(1);
            OnTypeChanged(1);
            (int shapeIdx, bool rotate) = storedShape switch
            {
                1 => (1, false),
                2 => (2, false),
                3 => (2, true),
                _ => (0, false),
            };
            _shapePicker.Select(shapeIdx);
            if (rotate)
                _landscapeButton.ButtonPressed = true;
            else
                _portraitButton.ButtonPressed = true;
        }

        _suppressShapeReset = false;

        _nameInput.Text = prototype.Name;
        _heightInput.Text = p.Height > 0 ? p.Height.ToString() : "";
        _widthInput.Text = p.Width > 0 ? p.Width.ToString() : "";
        _thicknessInput.Text = p.Thickness.ToString();

        _front.Side = p.Front;
        _sameAsFront.ButtonPressed = p.Back == null;
        if (p.Back != null)
            _back.Side = p.Back;
        RecordService.Instance.QueueSync(this);

        UpdateDimensionUI();
        Activate();
    }

    public override List<string> ValidateParameters(ComponentParameters parameters)
    {
        var ret = new List<string>();
        var p = parameters as PrintedParameters;

        if (string.IsNullOrEmpty(p?.ComponentName))
            ret.Add("Name may not be blank");
        if ((p?.Height ?? 0) <= 0)
            ret.Add("Height must be > 0");
        if ((p?.Width ?? 0) <= 0)
            ret.Add("Width must be > 0");

        return ret;
    }
}

using System.Collections.Generic;
using Godot;

namespace Lizzie;

public partial class ProjectSettingsDialog : Window, ICommandView
{
    /// <summary>
    /// The settings when the window was opened.
    /// Used to detect which fields were edited.
    /// </summary>
    private ProjectGameSettings _baseline;

    // Setup tab
    private CheckButton _2dToggle;
    private CheckButton _playerHandsToggle;
    private LineEdit _tableWidth;
    private LineEdit _tableHeight;
    private OptionButton _tableUnits;
    private ColorPickerButton _tableColor;
    private OptionButton _rotationStep;

    // Game Info tab
    private LineEdit _gameTitle;
    private LineEdit _designers;
    private LineEdit _graphicDesign;
    private LineEdit _artists;
    private LineEdit _contactInfo;
    private TextEdit _visionStatement;

    // Buttons
    private Button _saveButton;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        // Setup tab
        _2dToggle = GetNode<CheckButton>("%StartIn2dToggle");
        _playerHandsToggle = GetNode<CheckButton>("%PlayerHandsToggle");
        _tableWidth = GetNode<LineEdit>("%TableWidth");
        _tableHeight = GetNode<LineEdit>("%TableHeight");
        _tableUnits = GetNode<OptionButton>("%TableUnits");
        _tableColor = GetNode<ColorPickerButton>("%TableColor");
        _rotationStep = GetNode<OptionButton>("%RotationStep");

        // Game Info tab
        _gameTitle = GetNode<LineEdit>("%GameTitle");
        _designers = GetNode<LineEdit>("%Designers");
        _graphicDesign = GetNode<LineEdit>("%GraphicDesign");
        _artists = GetNode<LineEdit>("%Artists");
        _contactInfo = GetNode<LineEdit>("%ContactInfo");
        _visionStatement = GetNode<TextEdit>("%VisionStatement");

        // Buttons
        _saveButton = GetNode<Button>("%SaveButton");

        _saveButton.Pressed += OnSavePressed;
        CloseRequested += QueueFree;
    }

    public override void _EnterTree()
    {
        Atoms.R.Watch(this, Sync);
    }

    private void Sync(IWatchReader R)
    {
        var next = R.Get(ProjectGameSettings.Only);
        if (_baseline == null)
        {
            Load(next);
            return;
        }

        var ui = ReadUi();
        var merged = MergeSettings(_baseline, ui, next);
        WriteUiDiff(ui, merged);
        _baseline = next; // safe: immutable, no clone
    }

    private void Load(ProjectGameSettings s)
    {
        _2dToggle.ButtonPressed = s.StartIn2D;
        _playerHandsToggle.ButtonPressed = s.EnablePlayerHands;
        _tableWidth.Text = s.TableWidth.ToString();
        _tableHeight.Text = s.TableHeight.ToString();
        _tableUnits.Selected = s.TableUnits;
        _tableColor.Color = s.TableColor;
        _rotationStep.Selected = s.RotationStepIndex;

        // Game Info tab
        _gameTitle.Text = s.GameTitle;
        _designers.Text = s.Designers;
        _graphicDesign.Text = s.GraphicDesign;
        _artists.Text = s.Artists;
        _contactInfo.Text = s.ContactInfo;
        _visionStatement.Text = s.VisionStatement;

        _baseline = s;
    }

    private void OnSavePressed()
    {
        if (ProjectService.Instance.CurrentProject != null)
            Atoms.R.Write(this, ReadUi());
        QueueFree();
    }

    /// <summary>
    /// Reads every widget into a fresh settings atom.
    /// </summary>
    private ProjectGameSettings ReadUi()
    {
        var current = Atoms.R.Get(ProjectGameSettings.Only);
        return current with
        {
            StartIn2D = _2dToggle.ButtonPressed,
            EnablePlayerHands = _playerHandsToggle.ButtonPressed,
            TableWidth = ParseFloat(_tableWidth.Text, current.TableWidth),
            TableHeight = ParseFloat(_tableHeight.Text, current.TableHeight),
            TableUnits = _tableUnits.Selected,
            TableColor = _tableColor.Color,
            RotationStepIndex = _rotationStep.Selected,
            GameTitle = _gameTitle.Text,
            Designers = _designers.Text,
            GraphicDesign = _graphicDesign.Text,
            Artists = _artists.Text,
            ContactInfo = _contactInfo.Text,
            VisionStatement = _visionStatement.Text,
        };
    }

    private static T Pick<T>(T ui, T baseline, T next) =>
        EqualityComparer<T>.Default.Equals(ui, baseline) ? next : ui;

    /// <summary>
    /// Three-way merge
    /// For each field, keep the UI value if the user edited it.
    /// Otherwise take the incoming value.
    /// </summary>
    private static ProjectGameSettings MergeSettings(
        ProjectGameSettings baseline,
        ProjectGameSettings ui,
        ProjectGameSettings next
    )
    {
        return next with
        {
            StartIn2D = Pick(ui.StartIn2D, baseline.StartIn2D, next.StartIn2D),
            EnablePlayerHands = Pick(
                ui.EnablePlayerHands,
                baseline.EnablePlayerHands,
                next.EnablePlayerHands
            ),
            TableWidth = Pick(ui.TableWidth, baseline.TableWidth, next.TableWidth),
            TableHeight = Pick(ui.TableHeight, baseline.TableHeight, next.TableHeight),
            TableUnits = Pick(ui.TableUnits, baseline.TableUnits, next.TableUnits),
            TableColor = Pick(ui.TableColor, baseline.TableColor, next.TableColor),
            RotationStepIndex = Pick(
                ui.RotationStepIndex,
                baseline.RotationStepIndex,
                next.RotationStepIndex
            ),
            GameTitle = Pick(ui.GameTitle, baseline.GameTitle, next.GameTitle),
            Designers = Pick(ui.Designers, baseline.Designers, next.Designers),
            GraphicDesign = Pick(ui.GraphicDesign, baseline.GraphicDesign, next.GraphicDesign),
            Artists = Pick(ui.Artists, baseline.Artists, next.Artists),
            ContactInfo = Pick(ui.ContactInfo, baseline.ContactInfo, next.ContactInfo),
            VisionStatement = Pick(
                ui.VisionStatement,
                baseline.VisionStatement,
                next.VisionStatement
            ),
        };
    }

    /// <summary>
    /// Writes only the widgets whose target value differs from what they currently show.
    /// </summary>
    private void WriteUiDiff(ProjectGameSettings current, ProjectGameSettings target)
    {
        if (target.StartIn2D != current.StartIn2D)
            _2dToggle.ButtonPressed = target.StartIn2D;
        if (target.EnablePlayerHands != current.EnablePlayerHands)
            _playerHandsToggle.ButtonPressed = target.EnablePlayerHands;
        if (target.TableWidth != current.TableWidth)
            _tableWidth.Text = target.TableWidth.ToString();
        if (target.TableHeight != current.TableHeight)
            _tableHeight.Text = target.TableHeight.ToString();
        if (target.TableUnits != current.TableUnits)
            _tableUnits.Selected = target.TableUnits;
        if (target.TableColor != current.TableColor)
            _tableColor.Color = target.TableColor;
        if (target.RotationStepIndex != current.RotationStepIndex)
            _rotationStep.Selected = target.RotationStepIndex;
        if (target.GameTitle != current.GameTitle)
            _gameTitle.Text = target.GameTitle;
        if (target.Designers != current.Designers)
            _designers.Text = target.Designers;
        if (target.GraphicDesign != current.GraphicDesign)
            _graphicDesign.Text = target.GraphicDesign;
        if (target.Artists != current.Artists)
            _artists.Text = target.Artists;
        if (target.ContactInfo != current.ContactInfo)
            _contactInfo.Text = target.ContactInfo;
        if (target.VisionStatement != current.VisionStatement)
            _visionStatement.Text = target.VisionStatement;
    }

    private static float ParseFloat(string text, float fallback) =>
        float.TryParse(text, out var v) ? v : fallback;
}

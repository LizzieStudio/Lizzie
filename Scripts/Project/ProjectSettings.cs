using Godot;
using System;

public partial class ProjectSettings : Window
{
	// Setup tab
	private CheckButton _2dToggle;
	private CheckButton _playerHandsToggle;
	private LineEdit _tableWidth;
	private LineEdit _tableHeight;
	private OptionButton _tableUnits;
	private ColorPickerButton _tableColor;
	private OptionButton _rotationStep;

	// Players tab
	private CheckButton _observerToggle;
	private LineEdit _maxPlayers;
	private HBoxContainer _playerContainer1;
	private LineEdit _playerName1;
	private ColorPickerButton _playerColor1;
	private HBoxContainer _playerContainer2;
	private LineEdit _playerName2;
	private ColorPickerButton _playerColor2;
	private HBoxContainer _playerContainer3;
	private LineEdit _playerName3;
	private ColorPickerButton _playerColor3;
	private HBoxContainer _playerContainer4;
	private LineEdit _playerName4;
	private ColorPickerButton _playerColor4;
	private HBoxContainer _playerContainer5;
	private LineEdit _playerName5;
	private ColorPickerButton _playerColor5;
	private HBoxContainer _playerContainer6;
	private LineEdit _playerName6;
	private ColorPickerButton _playerColor6;
	private HBoxContainer _playerContainer7;
	private LineEdit _playerName7;
	private ColorPickerButton _playerColor7;
	private HBoxContainer _playerContainer8;
	private LineEdit _playerName8;
	private ColorPickerButton _playerColor8;

	// Game Info tab
	private LineEdit _gameTitle;
	private LineEdit _designers;
	private LineEdit _graphicDesign;
	private LineEdit _artists;
	private LineEdit _contactInfo;
	private TextEdit _visionStatement;

	// Buttons
	private Button _saveButton;
	private Button _cancelButton;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// Setup tab
		_2dToggle          = GetNode<CheckButton>("%StartIn2dToggle");
		_playerHandsToggle = GetNode<CheckButton>("%PlayerHandsToggle");
		_tableWidth        = GetNode<LineEdit>("%TableWidth");
		_tableHeight       = GetNode<LineEdit>("%TableHeight");
		_tableUnits        = GetNode<OptionButton>("%TableUnits");
		_tableColor        = GetNode<ColorPickerButton>("%TableColor");
		_rotationStep      = GetNode<OptionButton>("%RotationStep");

		// Players tab
		_observerToggle    = GetNode<CheckButton>("%ObserverToggle");
		_maxPlayers        = GetNode<LineEdit>("%MaxPlayers");
		_playerContainer1  = GetNode<HBoxContainer>("%PlayerContainer1");
		_playerName1       = GetNode<LineEdit>("%PlayerName1");
		_playerColor1      = GetNode<ColorPickerButton>("%PlayerColor1");
		_playerContainer2  = GetNode<HBoxContainer>("%PlayerContainer2");
		_playerName2       = GetNode<LineEdit>("%PlayerName2");
		_playerColor2      = GetNode<ColorPickerButton>("%PlayerColor2");
		_playerContainer3  = GetNode<HBoxContainer>("%PlayerContainer3");
		_playerName3       = GetNode<LineEdit>("%PlayerName3");
		_playerColor3      = GetNode<ColorPickerButton>("%PlayerColor3");
		_playerContainer4  = GetNode<HBoxContainer>("%PlayerContainer4");
		_playerName4       = GetNode<LineEdit>("%PlayerName4");
		_playerColor4      = GetNode<ColorPickerButton>("%PlayerColor4");
		_playerContainer5  = GetNode<HBoxContainer>("%PlayerContainer5");
		_playerName5       = GetNode<LineEdit>("%PlayerName5");
		_playerColor5      = GetNode<ColorPickerButton>("%PlayerColor5");
		_playerContainer6  = GetNode<HBoxContainer>("%PlayerContainer6");
		_playerName6       = GetNode<LineEdit>("%PlayerName6");
		_playerColor6      = GetNode<ColorPickerButton>("%PlayerColor6");
		_playerContainer7  = GetNode<HBoxContainer>("%PlayerContainer7");
		_playerName7       = GetNode<LineEdit>("%PlayerName7");
		_playerColor7      = GetNode<ColorPickerButton>("%PlayerColor7");
		_playerContainer8  = GetNode<HBoxContainer>("%PlayerContainer8");
		_playerName8       = GetNode<LineEdit>("%PlayerName8");
		_playerColor8      = GetNode<ColorPickerButton>("%PlayerColor8");

		// Game Info tab
		_gameTitle       = GetNode<LineEdit>("%GameTitle");
		_designers       = GetNode<LineEdit>("%Designers");
		_graphicDesign   = GetNode<LineEdit>("%GraphicDesign");
		_artists         = GetNode<LineEdit>("%Artists");
		_contactInfo     = GetNode<LineEdit>("%ContactInfo");
		_visionStatement = GetNode<TextEdit>("%VisionStatement");

		// Buttons
		_saveButton   = GetNode<Button>("%SaveButton");
		_cancelButton = GetNode<Button>("%CancelButton");

		_saveButton.Pressed   += OnSavePressed;
		_cancelButton.Pressed += OnClosePressed;
		CloseRequested        += OnClosePressed;

		LoadFromProject();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta) { }

	public event EventHandler Closed;

	private void OnClosePressed()
	{
		Closed?.Invoke(this, EventArgs.Empty);
	}

	private void LoadFromProject()
	{
		var s = ProjectService.Instance.CurrentProject?.GameSettings;
		if (s == null) return;

		_2dToggle.ButtonPressed         = s.StartIn2D;
		_playerHandsToggle.ButtonPressed = s.EnablePlayerHands;
		_tableWidth.Text                 = s.TableWidth.ToString();
		_tableHeight.Text                = s.TableHeight.ToString();
		_tableUnits.Selected             = s.TableUnits;
		_tableColor.Color                = new Color(s.TableColorR, s.TableColorG, s.TableColorB, s.TableColorA);
		_rotationStep.Selected           = s.RotationStepIndex;

		_observerToggle.ButtonPressed = s.AllowObservers;
		_maxPlayers.Text              = s.MaxPlayers.ToString();

		// Game Info tab
		_gameTitle.Text            = s.GameTitle;
		_designers.Text            = s.Designers;
		_graphicDesign.Text        = s.GraphicDesign;
		_artists.Text              = s.Artists;
		_contactInfo.Text          = s.ContactInfo;
		_visionStatement.Text      = s.VisionStatement;

		SetPlayer(_playerName1, _playerColor1, s, 0);
		SetPlayer(_playerName2, _playerColor2, s, 1);
		SetPlayer(_playerName3, _playerColor3, s, 2);
		SetPlayer(_playerName4, _playerColor4, s, 3);
		SetPlayer(_playerName5, _playerColor5, s, 4);
		SetPlayer(_playerName6, _playerColor6, s, 5);
		SetPlayer(_playerName7, _playerColor7, s, 6);
		SetPlayer(_playerName8, _playerColor8, s, 7);
	}

	private static void SetPlayer(LineEdit nameEdit, ColorPickerButton colorPicker, ProjectGameSettings s, int index)
	{
		if (index >= s.Players.Count) return;
		var p = s.Players[index];
		nameEdit.Text      = p.Name;
		colorPicker.Color  = new Color(p.ColorR, p.ColorG, p.ColorB, p.ColorA);
	}

	private void OnSavePressed()
	{
		var project = ProjectService.Instance.CurrentProject;
		if (project == null)
		{
			OnClosePressed();
			return;
		}

		var s = project.GameSettings;

		s.StartIn2D         = _2dToggle.ButtonPressed;
		s.EnablePlayerHands = _playerHandsToggle.ButtonPressed;
		s.TableWidth        = ParseFloat(_tableWidth.Text, s.TableWidth);
		s.TableHeight       = ParseFloat(_tableHeight.Text, s.TableHeight);
		s.TableUnits        = _tableUnits.Selected;
		s.TableColorR       = _tableColor.Color.R;
		s.TableColorG       = _tableColor.Color.G;
		s.TableColorB       = _tableColor.Color.B;
		s.TableColorA       = _tableColor.Color.A;
		s.RotationStepIndex = _rotationStep.Selected;

		s.AllowObservers = _observerToggle.ButtonPressed;
		s.MaxPlayers     = ParseInt(_maxPlayers.Text, s.MaxPlayers);

		SavePlayer(s, 0, _playerName1, _playerColor1);
		SavePlayer(s, 1, _playerName2, _playerColor2);
		SavePlayer(s, 2, _playerName3, _playerColor3);
		SavePlayer(s, 3, _playerName4, _playerColor4);
		SavePlayer(s, 4, _playerName5, _playerColor5);
		SavePlayer(s, 5, _playerName6, _playerColor6);
		SavePlayer(s, 6, _playerName7, _playerColor7);
		SavePlayer(s, 7, _playerName8, _playerColor8);

		// Game Info tab
		s.GameTitle        = _gameTitle.Text;
		s.Designers        = _designers.Text;
		s.GraphicDesign    = _graphicDesign.Text;
		s.Artists          = _artists.Text;
		s.ContactInfo      = _contactInfo.Text;
		s.VisionStatement  = _visionStatement.Text;

		ProjectService.Instance.SaveProject(project);
		OnClosePressed();
	}

	private static void SavePlayer(ProjectGameSettings s, int index, LineEdit nameEdit, ColorPickerButton colorPicker)
	{
		while (s.Players.Count <= index)
			s.Players.Add(new ProjectPlayerSettings());

		var p = s.Players[index];
		p.Name   = nameEdit.Text;
		p.ColorR = colorPicker.Color.R;
		p.ColorG = colorPicker.Color.G;
		p.ColorB = colorPicker.Color.B;
		p.ColorA = colorPicker.Color.A;
	}

	private static float ParseFloat(string text, float fallback) =>
		float.TryParse(text, out var v) ? v : fallback;

	private static int ParseInt(string text, int fallback) =>
		int.TryParse(text, out var v) ? v : fallback;
}



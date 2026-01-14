using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using TTSS.Scripts.Templating;
using FileAccess = Godot.FileAccess;

public partial class ProjectManager : Panel
{
	private Button _createButton;
	private Button _closeButton;
	private Button _openButton;

	private HBoxContainer _createPanel;
	private Button _createCancel;
	private Button _createExecute;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_createPanel = GetNode<HBoxContainer>("%CreateProject");
		_createExecute = GetNode<Button>("%CreateProjectButton");
		_createExecute.Pressed += CreateExecuteOnPressed;
		
		_createCancel = GetNode<Button>("%CancelCreateButton");
		_createCancel.Pressed += () => _createPanel.Hide();
		
		_createButton = GetNode<Button>("%CreateButton");
		_createButton.Pressed += () => _createPanel.Show();
		
			
			
		_closeButton = GetNode<Button>("%CloseButton");
		_closeButton.Pressed += Hide;
		_openButton = GetNode<Button>("%OpenButton");
	}

	private void CreateExecuteOnPressed()
	{
		//TODO create project
		_createPanel.Hide();
	}

	public Project CreateTestProject()
	{
		var p = new Project
		{
			Name = "Test Project"
		};

		var t = new Template
		{
			Name = "Issue Face",
			Description = "A face with an issue",
			Height = 350,
			Width = 250
		};

		var d1 = new Dictionary<string, string>
		{
			{ "Name", "Header" },
			{"X", "20"},
			{"Y", "20"},
			{ "Width", "100" },
			{ "Height", "50" },
			{ "Text", "Issue" }
		};
		
		var d2 = new Dictionary<string, string>
		{
			{ "Name", "Image" },
			{"X", "20"},
			{"Y", "20"},
			{ "Width", "100" },
			{ "Height", "50" },
			{ "Text", "Heart" }
		};
		
		t.Elements.Add(d1);
		t.Elements.Add(d2);

		p.Templates.Add("Issue Face", t);
		
		return p;
	}

	public Project CurrentProject { get; set; }

	public Project LoadProject(string name)
	{
		if (!FileAccess.FileExists($"user://{name}.proj"))
		{
			return null; // Error! We don't have a save to load.
		}

		using var saveFile = FileAccess.Open($"user://{name}.proj", FileAccess.ModeFlags.Read);

		var s = saveFile.GetAsText();
		
		return JsonSerializer.Deserialize<Project>(s);	
	}

	public bool SaveProject(Project project, string fileName)
	{
		using var saveFile = FileAccess.Open($"user://{fileName}.proj", FileAccess.ModeFlags.Write);

		var s = JsonSerializer.Serialize<Project>(project);

		saveFile.StoreString(s);
		saveFile.Close();
		
		return true;
	}
	
}

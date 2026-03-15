using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Godot;
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
        _closeButton.Pressed += OnClose;
        _openButton = GetNode<Button>("%OpenButton");
    }

    private void CreateExecuteOnPressed()
    {
        //TODO create project
        return;
    }

    public event EventHandler Closed;

    private void OnClose()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    public Project CreateTestProject()
    {
        var p = new Project { Name = "Test Project" };

        var t = new Template
        {
            Name = "Issue Face",
            Description = "A face with an issue",
            Height = 350,
            Width = 250,
            SizeTemplate = "Poker",
        };

        var d1 = new Dictionary<string, string>
        {
            { "Id", "1" },
            { "Name", "Header" },
            { "Type", "Text" },
            { "X", "20" },
            { "Y", "20" },
            { "Width", "100" },
            { "Height", "50" },
            { "Text", "Issue" },
        };

        var d2 = new Dictionary<string, string>
        {
            { "Id", "2" },
            { "Name", "Image" },
            { "Type", "Image" },
            { "X", "20" },
            { "Y", "20" },
            { "Width", "100" },
            { "Height", "50" },
            { "Text", "Heart" },
        };

        t.Elements.Add(d1);
        t.Elements.Add(d2);

        p.Templates.Add("Issue Face", t);

        p.Datasets.Add("Test Data", DataSet.TestDataSet());

        return p;
    }
}

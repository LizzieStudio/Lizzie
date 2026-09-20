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
}

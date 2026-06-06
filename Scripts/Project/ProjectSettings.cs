using Godot;
using System;

public partial class ProjectSettings : Window
{
    private Button _saveButton;
    public Button _cancelButton;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_saveButton = GetNode<Button>("%SaveButton");
        _cancelButton = GetNode<Button>("%CancelButton");

        _saveButton.Pressed += OnSavePressed;
        _cancelButton.Pressed += OnClosePressed;

        CloseRequested += OnClosePressed;
    }

    private void OnSavePressed()
    {
        //TOOD save the settings
        OnClosePressed();
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

    public event EventHandler Closed;

    private void OnClosePressed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }
}

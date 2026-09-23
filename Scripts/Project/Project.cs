using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Godot;
using TTSS.Scripts.Templating;

public class Project
{
    public string Filename { get; set; }

    /// <summary>
    /// The snapshot currently loaded or <see cref="SnowTag.Empty"/>.
    /// </summary>
    public SnowTag ActiveGameState { get; set; }

    /// <summary>
    /// Project-level settings edited via the Project Settings dialog.
    /// </summary>
    public ProjectGameSettings GameSettings { get; set; } = new();
}

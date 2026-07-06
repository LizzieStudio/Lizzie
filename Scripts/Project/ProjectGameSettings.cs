using System.Collections.Generic;

/// <summary>
/// Persisted settings for a project, edited via the Project Settings dialog.
/// </summary>
public class ProjectGameSettings
{
    // Setup tab
    public bool StartIn2D { get; set; } = false;
    public bool EnablePlayerHands { get; set; } = false;
    public float TableWidth { get; set; } = 3f;
    public float TableHeight { get; set; } = 3f;
    /// <summary>0 = Feet, 1 = Meters</summary>
    public int TableUnits { get; set; } = 0;
    public float TableColorR { get; set; } = 0.28f;
    public float TableColorG { get; set; } = 0.60f;
    public float TableColorB { get; set; } = 0.41f;
    public float TableColorA { get; set; } = 1f;
    /// <summary>Index into the rotation-step option list (15°, 30°, 45°, 60°, 90°, 180°).</summary>
    public int RotationStepIndex { get; set; } = 0;

    // Players tab
    public bool AllowObservers { get; set; } = false;
    public int MaxPlayers { get; set; } = 4;
    public List<ProjectPlayerSettings> Players { get; set; } = new()
    {
        new ProjectPlayerSettings { Name = "Seat 1", ColorR = 0f, ColorG = 0f, ColorB = 1f },
        new ProjectPlayerSettings { Name = "Seat 2", ColorR = 1f, ColorG = 0f, ColorB = 0f },
        new ProjectPlayerSettings { Name = "Seat 3", ColorR = 0f, ColorG = 1f, ColorB = 0f },
        new ProjectPlayerSettings { Name = "Seat 4", ColorR = 1f, ColorG = 1f, ColorB = 0f },
    };

    // Game Info tab
    public string GameTitle { get; set; } = string.Empty;
    public string Designers { get; set; } = string.Empty;
    public string GraphicDesign { get; set; } = string.Empty;
    public string Artists { get; set; } = string.Empty;
    public string ContactInfo { get; set; } = string.Empty;
    public string VisionStatement { get; set; } = string.Empty;
}

public class ProjectPlayerSettings
{
    public string Name { get; set; } = string.Empty;
    public float ColorR { get; set; } = 1f;
    public float ColorG { get; set; } = 1f;
    public float ColorB { get; set; } = 1f;
    public float ColorA { get; set; } = 1f;
}

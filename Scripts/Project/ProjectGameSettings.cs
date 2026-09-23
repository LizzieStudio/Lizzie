using System.Collections.Immutable;
using System.Linq;
using Godot;

/// <summary>
/// Persisted settings for a project, edited via the Project Settings dialog.
/// </summary>
public record ProjectGameSettings
{
    // Setup tab
    public bool StartIn2D { get; init; } = false;
    public bool EnablePlayerHands { get; init; } = false;
    public float TableWidth { get; init; } = 3f;
    public float TableHeight { get; init; } = 3f;

    /// <summary>0 = Feet, 1 = Meters</summary>
    public int TableUnits { get; init; } = 0;

    public Color TableColor { get; init; } = new Color(0.28f, 0.60f, 0.41f, 1f);

    /// <summary>Index into the rotation-step option list (15°, 30°, 45°, 60°, 90°, 180°).</summary>
    public int RotationStepIndex { get; init; } = 0;

    // Players tab
    public bool AllowObservers { get; init; } = false;
    public int MaxPlayers { get; init; } = 4;

    public ImmutableArray<ProjectPlayerSettings> Players { get; init; } =
        new[] { 0, 1, 2, 3 }
            .Select(
                (n) =>
                    new ProjectPlayerSettings
                    {
                        Name = $"Seat {n}",
                        // 13/34 is roughly the golden angle. It picks distinct colors on the color wheel.
                        Color = Color.FromOkHsl(13f / 34f * n, 1.0f, 0.7f),
                    }
            )
            .ToImmutableArray();

    // Game Info tab
    public string GameTitle { get; init; } = string.Empty;
    public string Designers { get; init; } = string.Empty;
    public string GraphicDesign { get; init; } = string.Empty;
    public string Artists { get; init; } = string.Empty;
    public string ContactInfo { get; init; } = string.Empty;
    public string VisionStatement { get; init; } = string.Empty;
}

public record ProjectPlayerSettings
{
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; } = new Color(1f, 1f, 1f, 1f);
    public bool IsAdmin { get; init; } = false;

    /// <summary>
    /// The container id for this seat's hand.
    /// The hand belongs to the seat, not the player.
    /// </summary>
    public SnowTag HandRef { get; init; } = SnowTag.Empty;

    /// <summary>
    /// The container id for this seat's cursor.
    /// The cursor belongs to the seat, not the player.
    /// </summary>
    public SnowTag CursorRef { get; init; } = SnowTag.Empty;
}

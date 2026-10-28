using System.Collections.Immutable;
using System.Linq;
using Godot;

namespace Lizzie;

/// <summary>
/// Persisted settings for a project, edited via the Project Settings dialog.
/// </summary>
[JsonName("ProjectGameSettings")]
public sealed record ProjectGameSettings : Atom
{
    public static readonly Singleton<ProjectGameSettings> Only = new();

    // Setup tab
    public bool StartIn2D { get; init; } = false;
    public bool EnablePlayerHands { get; init; } = false;
    public float TableWidth { get; init; } = 3f;
    public float TableHeight { get; init; } = 3f;

    /// <summary>0 = Feet, 1 = Meters</summary>
    public int TableUnits { get; init; } = 0;

    public Color TableColor { get; init; } = new Color(0.28f, 0.60f, 0.41f, 1f);

    /// <summary>
    /// The rotation steps to choose from, in degrees, in the order the rotation-step dropdowns list them.
    /// </summary>
    public static readonly ImmutableArray<float> RotationSteps = [15, 30, 45, 60, 90, 180];

    /// <summary>
    /// Index into <see cref="RotationSteps"/>.
    /// </summary>
    public int RotationStepIndex { get; init; } = 0;

    /// <summary>
    /// The current rotation step.
    /// </summary>
    public float RotationStep() => RotationSteps[RotationStepIndex];

    // Game Info tab
    public string GameTitle { get; init; } = string.Empty;
    public string Designers { get; init; } = string.Empty;
    public string GraphicDesign { get; init; } = string.Empty;
    public string Artists { get; init; } = string.Empty;
    public string ContactInfo { get; init; } = string.Empty;
    public string VisionStatement { get; init; } = string.Empty;
}

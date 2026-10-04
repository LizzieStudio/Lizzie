/// <summary>
/// Points at the snapshot currently loaded, or <see cref="SnowTag.Empty"/>.
/// </summary>
[Singleton]
public sealed record ActiveGameStateRef : Replicated
{
    public SnowTag GameStateId { get; init; } = SnowTag.Empty;
}

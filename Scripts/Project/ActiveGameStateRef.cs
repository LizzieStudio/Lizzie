/// <summary>
/// Points at the snapshot currently loaded, or <see cref="SnowTag.Empty"/>.
/// </summary>
public sealed record ActiveGameStateRef
{
    public SnowTag Id { get; init; } = SnowTag.Empty;
}

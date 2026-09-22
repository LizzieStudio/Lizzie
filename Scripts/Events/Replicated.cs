public abstract record Replicated : IReplicated
{
    public SnowTag Id { get; init; }

    public bool Deleted { get; init; }

    public SnowportId LastUpdateId { get; init; }

    public IReplicated WithIdentity(SnowTag id, SnowportId lastUpdateId) =>
        this with
        {
            Id = id,
            LastUpdateId = lastUpdateId,
        };

    public string SheetKey() => $"{Id.Value:X8}{LastUpdateId.Value:X16}";
}

using System.Collections.Generic;

/// <summary>
/// Collects replicated upserts for submission.
/// </summary>
public sealed class UpsertBatch
{
    private readonly SnowportId _eventId = Snowport.Clock.Create();
    private readonly List<Effect> _effects = new();

    /// <summary>Adds an upsert of a replicated definition. Mints an id when the entity lacks one.</summary>
    public UpsertBatch Add<T>(T entity)
        where T : class, IReplicated
    {
        if (entity == null)
            return this;

        var id = entity.Id == SnowTag.Empty ? Snowport.Clock.CreateTag() : entity.Id;
        entity = (T)entity.WithIdentity(id, _eventId);
        _effects.Add(new UpdateReplicatedEffect<T> { Id = id, Payload = entity });
        return this;
    }

    /// <summary>Submits the collected effects as one event. A no-op when nothing was added.</summary>
    public void Submit()
    {
        if (_effects.Count == 0)
            return;

        EventSynchronizer.Instance?.Submit(
            new TableEvent
            {
                Id = _eventId,
                Action = null,
                Effects = _effects.ToArray(),
            }
        );
    }
}

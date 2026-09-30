using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// What one player has selected in one place, which every player sees.
/// Each player writes only their own, so selections never overwrite each other.
/// Selections are not saved with the project.
/// </summary>
public record Selection : Replicated
{
    /// <summary>The source of the player who owns this selection.</summary>
    [JsonPropertyName("p")]
    public byte Player { get; init; }

    /// <summary>
    /// Where it was made: empty for the table, or the dataset whose rows and columns it holds.
    /// A player has one selection in each place, so each view undoes only its own.
    /// </summary>
    [JsonPropertyName("w")]
    public SnowTag Within { get; init; }

    /// <summary>The selected things.</summary>
    [JsonPropertyName("t")]
    public ImmutableHashSet<Target> Targets { get; init; } = ImmutableHashSet<Target>.Empty;
}

/// <summary>
/// Reads and writes the local player's <see cref="Selection"/>.
/// </summary>
public static class LocalSelection
{
    /// <summary>
    /// The local player's selection <paramref name="within"/> a dataset, or on the table by default.
    /// Null if they have not yet selected anything there.
    /// </summary>
    public static Selection GetSelection(this IRecordReader R, SnowTag within = default) =>
        R.Get<Selection>(s => s.Player == Snowport.Clock.source && s.Within == within)
            .FirstOrDefault();

    /// <summary>The ids of the records of type <typeparamref name="T"/> the local player has selected on the table.</summary>
    public static IEnumerable<SnowTag> GetSelection<T>(this IRecordReader R)
        where T : class, IReplicated =>
        R.GetSelection()?.Targets.OfType<RecordTarget>().Select(t => t.Id).Where(id => R.Is<T>(id))
        ?? [];

    /// <summary>
    /// Replaces the local player's selected records of type <typeparamref name="T"/> on the table,
    /// keeping everything else they have selected there.
    /// </summary>
    public static void SetSelection<T>(this IRecordReader R, IEnumerable<SnowTag> ids)
        where T : class, IReplicated
    {
        var old = R.GetSelection()?.Targets ?? ImmutableHashSet<Target>.Empty;
        R.SetSelection(
            old.Where(t => t is not RecordTarget r || !R.Is<T>(r.Id))
                .Concat(ids.Select(id => new RecordTarget(id)))
        );
    }

    /// <summary>
    /// Replaces the local player's whole selection <paramref name="within"/> a dataset, or on the table by default.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection(
        this IRecordReader R,
        IEnumerable<Target> targets,
        SnowTag within = default
    )
    {
        var current = R.GetSelection(within);
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<Target>.Empty))
            return;

        var record =
            current
            ?? new Selection
            {
                Id = Snowport.Clock.CreateTag(),
                Player = Snowport.Clock.source,
                Within = within,
            };
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now([Effect.Upsert(record with { Targets = next })])
        );
    }
}

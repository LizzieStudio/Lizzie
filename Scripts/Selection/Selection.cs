using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// What one player has selected, which every player sees.
/// Each player writes only their own, so selections never overwrite each other.
/// Selections are not saved with the project.
/// </summary>
public record Selection : Replicated
{
    /// <summary>The source of the player who owns this selection.</summary>
    [JsonPropertyName("p")]
    public byte Player { get; init; }

    /// <summary>The selected things, of every kind. Each view reads the kinds it shows.</summary>
    [JsonPropertyName("t")]
    public ImmutableHashSet<Target> Targets { get; init; } = ImmutableHashSet<Target>.Empty;
}

/// <summary>
/// Reads and writes the local player's <see cref="Selection"/>.
/// </summary>
public static class LocalSelection
{
    /// <summary>The local player's selection, or null if they have not yet selected anything.</summary>
    public static Selection GetSelection(this IRecordReader R) =>
        R.Get<Selection>(s => s.Player == Snowport.Clock.source).FirstOrDefault();

    /// <summary>The ids of the records of type <typeparamref name="T"/> the local player has selected.</summary>
    public static IEnumerable<SnowTag> GetSelection<T>(this IRecordReader R)
        where T : class, IReplicated =>
        R.GetSelection()?.Targets.OfType<RecordTarget>().Select(t => t.Id).Where(id => R.Is<T>(id))
        ?? [];

    /// <summary>
    /// Replaces the local player's selected records of type <typeparamref name="T"/>,
    /// keeping everything else they have selected.
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
    /// Replaces the local player's whole selection.
    /// Submits an event only when it changes, which undo can reverse like any other.
    /// </summary>
    public static void SetSelection(this IRecordReader R, IEnumerable<Target> targets)
    {
        var current = R.GetSelection();
        var next = targets.ToImmutableHashSet();
        if (next.SetEquals(current?.Targets ?? ImmutableHashSet<Target>.Empty))
            return;

        var record =
            current
            ?? new Selection { Id = Snowport.Clock.CreateTag(), Player = Snowport.Clock.source };
        EventSynchronizer.Instance?.Submit(
            TableEvent.Now(null, [Effect.Upsert(record with { Targets = next })])
        );
    }
}

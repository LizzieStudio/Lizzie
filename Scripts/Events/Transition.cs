using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Marks writes that are drafts and not intended to be kept.
/// Started by <see cref="Atoms.Open(Node, IEnumerable{Atom})"/>.
/// </summary>
/// <param name="Start">The id of the transition's first event.</param>
[JsonConverter(typeof(Json))]
public readonly record struct Transition(SnowStamp Start) : IComparable<Transition>
{
    /// <summary>
    /// A placeholder meaning no transition.
    /// </summary>
    public static readonly Transition None = default;

    public int CompareTo(Transition other) => Start.CompareTo(other.Start);

    public override string ToString() => Start.ToString();

    private sealed class Json : JsonConverter<Transition>
    {
        public override void Write(
            Utf8JsonWriter writer,
            Transition transition,
            JsonSerializerOptions options
        ) => JsonSerializer.Serialize(writer, transition.Start, options);

        public override Transition Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(JsonSerializer.Deserialize<SnowStamp>(ref reader, options));
    }
}

/// <summary>
/// The immutable set of transitions a write is a draft of.
/// </summary>
/// <remarks>
/// A write only counts when it has no transitions or while every transition in its set is open.
/// </remarks>
[JsonConverter(typeof(Json))]
[CollectionBuilder(typeof(TransitionSet), nameof(Create))]
public readonly record struct TransitionSet : IEnumerable<Transition>
{
    // we keep it in order for equality checks
    private readonly ImmutableArray<Transition> _sorted;

    private TransitionSet(ImmutableArray<Transition> sorted) => _sorted = sorted;

    private ImmutableArray<Transition> Sorted => _sorted.IsDefault ? [] : _sorted;

    public bool IsEmpty => Sorted.IsEmpty;

    public bool Contains(Transition transition) => Sorted.BinarySearch(transition) >= 0;

    /// <summary>
    /// Returns this set with <paramref name="transition"/> added.
    /// </summary>
    public TransitionSet With(Transition transition)
    {
        int at = Sorted.BinarySearch(transition);
        return at >= 0 ? this : new(Sorted.Insert(~at, transition));
    }

    /// <summary>
    /// Returns this set with <paramref name="transition"/> removed.
    /// </summary>
    public TransitionSet Without(Transition transition)
    {
        int at = Sorted.BinarySearch(transition);
        return at < 0 ? this : new(Sorted.RemoveAt(at));
    }

    public ImmutableArray<Transition>.Enumerator GetEnumerator() => Sorted.GetEnumerator();

    public bool Equals(TransitionSet other) => Sorted.SequenceEqual(other.Sorted);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var transition in Sorted)
            hash.Add(transition);
        return hash.ToHashCode();
    }

    public override string ToString() => $"[{string.Join(", ", Sorted)}]";

    private sealed class Json : JsonConverter<TransitionSet>
    {
        public override void Write(
            Utf8JsonWriter writer,
            TransitionSet set,
            JsonSerializerOptions options
        ) => JsonSerializer.Serialize(writer, set.Sorted, options);

        public override TransitionSet Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(JsonSerializer.Deserialize<ImmutableArray<Transition>>(ref reader, options));
    }

    #region Unit Testing

    // This lets us write transitions as [a, b, c, ...], which is useful in tests.
    public static TransitionSet Create(ReadOnlySpan<Transition> transitions)
    {
        var set = default(TransitionSet);
        foreach (var transition in transitions)
            set = set.With(transition);
        return set;
    }

    IEnumerator<Transition> IEnumerable<Transition>.GetEnumerator() =>
        ((IEnumerable<Transition>)Sorted).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Sorted).GetEnumerator();

    #endregion
}

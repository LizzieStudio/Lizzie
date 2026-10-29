using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace Lizzie;

/// <summary>
/// Ordered ranks for indexing atoms.
///
/// <para>
/// Unlike with numeric indexes, a SnowRank doesn't require you to update any existing atoms to insert a new one.
/// </para>
/// </summary>
/// <remarks>
/// Ranks sort in that order with <see cref="CompareTo"/>. The default rank has no steps and sorts first.
/// Places are partly based on LexoRanks.
/// </remarks>
public readonly record struct SnowRank : IComparable<SnowRank>
{
    private enum RankSide
    {
        Start,
        End,
    }

    private readonly record struct RankStep(SnowportId Stamp, string Place);

    // These are in Ordinal order.
    // We can never end a place with the lowest character <c>*</c>, since nothing could go before it.
    // At the high end, more characters make a place go higher: <c>z < z*</c>
    // At the low end, fewer characters would be required to make a place go lower: <c>V < V*</c>
    // The problem is that we can't guarantee that that place won't be taken.
    // We can always add more characters to make a unique place, but not necessarily the other way around.
    // Therefore, we have to never end a place with the lowest character. We only use it for the occasional middle character.
    // Suborders use the 64 characters <c>-</c> through <c>z</c>, one per 6 bits.
    private const string Alphabet =
        "*-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Min_Digit = 0;
    private const int Max_Digit = 65;

    private readonly ImmutableArray<RankStep> _steps;

    [JsonInclude]
    private RankSide Side { get; init; }

    [JsonInclude]
    private ImmutableArray<RankStep> Steps
    {
        get => _steps.IsDefault ? [] : _steps;
        init => _steps = value;
    }

    public int CompareTo(SnowRank other)
    {
        if (Side != other.Side)
            return Side.CompareTo(other.Side);
        for (int k = 0; k < Steps.Length && k < other.Steps.Length; k++)
        {
            int stamps = Steps[k].Stamp.CompareTo(other.Steps[k].Stamp);
            if (stamps != 0)
                return k == 0 && Side == RankSide.End ? stamps : -stamps;
            int places = string.CompareOrdinal(Steps[k].Place, other.Steps[k].Place);
            if (places != 0)
                return places;
        }
        return Steps.Length.CompareTo(other.Steps.Length);
    }

    public bool Equals(SnowRank other) => Side == other.Side && Steps.SequenceEqual(other.Steps);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Side);
        foreach (var step in Steps)
            hash.Add(step);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Side}[{string.Join(", ", Steps.Select(s => $"{s.Stamp}:{s.Place}"))}]";

    /// <summary>
    /// Gets ranks before all that exist, anywhere.
    /// </summary>
    /// <param name="count">How many ranks to get back.</param>
    public static IReadOnlyList<SnowRank> BeforeAll(int count) => Made(RankSide.Start, [], count);

    /// <summary>
    /// Gets ranks after all that exist, anywhere.
    /// </summary>
    /// <param name="count">How many ranks to get back.</param>
    public static IReadOnlyList<SnowRank> AfterAll(int count) => Made(RankSide.End, [], count);

    /// <summary>
    /// Gets the rank that would put a new item at <paramref name="index"/> among <paramref name="ranked"/>.
    /// </summary>
    /// <param name="ranked">The sorted list of atoms you're inserting into.</param>
    /// <param name="index">The index at which to insert. If it equals the Count, inserts at the end.</param>
    public static SnowRank At(IReadOnlyList<IRanked> ranked, int index) => At(ranked, index, 1)[0];

    /// <summary>
    /// Gets <paramref name="count"/> ranks, in order, that would put new items at <paramref name="index"/> among <paramref name="ranked"/>.
    /// </summary>
    /// <param name="ranked">The sorted list of atoms you're inserting into.</param>
    /// <param name="index">The index at which to insert. If it equals the Count, inserts at the end.</param>
    public static IReadOnlyList<SnowRank> At(IReadOnlyList<IRanked> ranked, int index, int count)
    {
        if (index == ranked.Count)
            return AfterAll(count);
        var prev = index > 0 ? ranked[index - 1].Rank : default;
        var next = ranked[index].Rank;
        // Every rank after prev is before a rank on the other side.
        var bound = prev.Side == next.Side ? next.Steps : [];

        // Copy the steps the two share, then go above prev's step where they part:
        // between the places if the bound has the same stamp there, or anywhere above otherwise.
        // When the bound extends prev, the new stamp alone goes between them, since a new stamp goes first.
        int shared = 0;
        while (
            shared < prev.Steps.Length
            && shared < bound.Length
            && prev.Steps[shared] == bound[shared]
        )
            shared++;
        var steps = prev.Steps.Take(shared).ToList();
        if (shared < prev.Steps.Length)
        {
            var parting = prev.Steps[shared];
            var above =
                shared < bound.Length && bound[shared].Stamp == parting.Stamp
                    ? bound[shared].Place
                    : "";
            steps.Add(parting with { Place = Between(parting.Place, above) });
        }
        return Made(prev.Side, steps, count);
    }

    private static IReadOnlyList<SnowRank> Made(
        RankSide side,
        IReadOnlyList<RankStep> steps,
        int count
    )
    {
        if (count == 0)
            return [];
        var stamp = SnowportId.Create();
        return
        [
            .. Enumerable
                .Range(0, count)
                .Select(i => new SnowRank
                {
                    Side = side,
                    Steps = [.. steps, new(stamp, Place(i))],
                }),
        ];
    }

    private static int Ord(char c) => Alphabet.IndexOf(c);

    private static char Chr(int i) => Alphabet[i];

    /// <summary>
    /// Converts a number into an Ordinal string.
    /// </summary>
    private static string Place(int suborder)
    {
        var digits = new StringBuilder();
        for (; suborder > 0; suborder >>= 6)
            digits.Insert(0, Chr((suborder & 63) + 1));
        return digits.Length == 0 ? "" : Chr(digits.Length) + digits.ToString();
    }

    private static string Between(string prev, string next)
    {
        var result = new StringBuilder();
        for (int i = 0; true; i++)
        {
            // if prev is out of digits, pad it with the min
            int p = i < prev.Length ? Ord(prev[i]) : Min_Digit;
            // if next is out of digits, pad it with the max
            int n = i < next.Length ? Ord(next[i]) : Max_Digit;

            if (p == n)
            {
                // If the strings are equal up to this point, use the same char and continue.
                // abc... + abc... = abc...
                result.Append(Chr(p));
            }
            else if (p == n - 1)
            {
                // If the strings are only 1 off, use p as the char so we're at least below next.
                // abc... + abd... = abc...
                result.Append(Chr(p));
                // We no longer need to limbo under next, so set it to empty to stop trying.
                next = string.Empty;
            }
            else
            {
                // If the strings are two or more off, there is enough room to go in the middle.
                // abc... + abe... = abd
                int mid = (p + n) / 2;
                result.Append(Chr(mid));
                return result.ToString();
            }
        }
    }
}

/// <summary>
/// Something kept in order by a <see cref="SnowRank"/>, like a dataset's rows and columns.
/// </summary>
public interface IRanked
{
    SnowRank Rank { get; }

    /// <summary>
    /// Orders a lookup by rank, like <c>new(r => r.DatasetRef, IRanked.ByRank)</c>.
    /// </summary>
    static int ByRank(IRanked a, IRanked b) => a.Rank.CompareTo(b.Rank);
}

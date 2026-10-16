using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Ordered ranks for indexing atoms.
///
/// <para>
/// If given two strings, it finds the string that is in-between them in "Ordinal" order:
/// <code>ABCD + ABEFG = ABD</code>
/// It then adds a new SpowportId to the end in base64 to guarantee uniqueness.
/// </para>
///
/// This ensures that you never have to update any existing atoms in order to insert one between them.
/// </summary>
/// <remarks>
/// Currently used by <see cref="DataRow"/> and <see cref="DataColumn"/>.
/// Ranks sort in that order with <see cref="CompareTo"/>. The default rank is empty and sorts first.
/// Partly based on LexoRanks.
/// </remarks>
[JsonConverter(typeof(Json))]
public readonly record struct SnowRank : IComparable<SnowRank>
{
    // These are in Ordinal order.
    // We can never end a rank with the lowest character <c>+</c>, since nothing could go before it.
    // At the high end, more characters make a rank go higher: <c>z < z+</c>
    // At the low end, fewer characters would be required to make a rank go lower: <c>V < V+</c>
    // The problem is that we can't guarentee that that rank won't be taken.
    // We can always add more characters to make a unique rank, but not necessarily the other way around.
    // Therefore, we have to never end a rank with the lowest character. We only use it for the occasional middle character.
    // Stamps use the 64 characters <c>-</c> through <c>z</c>, one per 6 bits.
    private const string Alphabet =
        "+-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Min_Digit = 0;
    private const int Max_Digit = 65;

    // A SnowportId has 53 bits, which takes 9 characters.
    // Every stamp has the same length, or a short rank plus its stamp could spell a long rank plus a different stamp.
    private const int StampLength = 9;

    private readonly string _value;

    private SnowRank(string value) => _value = value;

    private string Value => _value ?? string.Empty;

    public int CompareTo(SnowRank other) => string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;

    /// <summary>
    /// A rank right after this one, ending with <paramref name="stamp"/>.
    /// </summary>
    public SnowRank After(SnowportId stamp) => New(this, default, stamp);

    /// <summary>
    /// Gets the rank that would put a new item at <paramref name="index"/> among <paramref name="ranked"/>.
    /// </summary>
    /// <param name="ranked">The sorted list of atoms you're inserting into.</param>
    /// <param name="index">The index at which to insert. If it equals the Count, inserts at the end.</param>
    /// <param name="stamp">The stamp to use in the rank, created with <see cref="Snowport.Create()"/>.</param>
    public static SnowRank At(IReadOnlyList<IRanked> ranked, int index, SnowportId stamp) =>
        New(
            index > 0 ? ranked[index - 1].Rank : default,
            index < ranked.Count ? ranked[index].Rank : default,
            stamp
        );

    private static int Ord(char c) => Alphabet.IndexOf(c);

    private static char Chr(int i) => Alphabet[i];

    /// <summary>
    /// Returns a rank strictly between <paramref name="prev"/> and <paramref name="next"/>, ending with <paramref name="stamp"/>.
    /// If <paramref name="prev"/> is the default, get a rank below <paramref name="next"/>.
    /// If <paramref name="next"/> is the default, get a rank above <paramref name="prev"/>.
    /// </summary>
    public static SnowRank New(SnowRank prev, SnowRank next, SnowportId stamp) =>
        // Added characters never change the order.
        // If ABC < ABD < ABE, then the same is true for any extension of ABD...
        new(Between(prev.Value, next.Value) + Stamp(stamp));

    /// <summary>
    /// The stamp's digits in reverse order.
    /// The lower digits change more often, which keeps ranks made between them short.
    /// </summary>
    private static string Stamp(SnowportId stamp)
    {
        long bits = stamp;
        var result = new StringBuilder(StampLength);
        for (int i = 0; i < StampLength; i++, bits >>= 6)
            result.Append(Chr((int)(bits & 63) + 1));
        return result.ToString();
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

    // Saved as its string, so a save reads the same as when ranks were strings.
    private sealed class Json : JsonConverter<SnowRank>
    {
        public override SnowRank Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => new(reader.GetString());

        public override void Write(
            Utf8JsonWriter writer,
            SnowRank rank,
            JsonSerializerOptions options
        ) => writer.WriteStringValue(rank.Value);
    }
}

/// <summary>
/// Something kept in order by a <see cref="SnowRank"/>, like a dataset's rows and columns.
/// </summary>
public interface IRanked
{
    SnowRank Rank { get; }
}

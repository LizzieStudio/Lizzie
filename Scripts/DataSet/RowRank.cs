using System.Collections.Generic;
using System.Text;

/// <summary>
/// "LexoRank" ordering keys for dataset rows.
///
/// If given two strings, it finds the string that is between them in "Ordinal" order:
/// ABCD + ABEFG = ABD
///
/// This ensures that you can always insert a row in-between two other rows if you need to.
///
/// <para>
/// Every rank ends with a character unique to the source that made it,
/// so two players inserting at roughly the same time still produce different ranks.
/// </para>
/// </summary>
public static class RowRank
{
    // These are in Ordinal order.
    // We can never end a rank with the lowest character <c>+</c>, since nothing could go before it.
    // At the high end, more characters make a rank go higher: <c>z < z+</c>
    // At the low end, fewer characters would be required to make a rank go lower: <c>V < V+</c>
    // The problem is that we can't guarentee that that rank won't be taken.
    // We can always add more characters to make a unique rank, but not necessarily the other way around.
    // Therefore, we have to never end a rank with the lowest character. We only use it for the occasional middle character.
    // The 64 unique source numbers then translate to <c>-</c> through <c>z</c>.
    private const string Alphabet =
        "+-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Min_Digit = 0;
    private const int Max_Digit = 65;

    public static readonly IComparer<string> Comparer = System.StringComparer.Ordinal;

    private static int Ord(char c) => Alphabet.IndexOf(c);

    private static char Chr(int i) => Alphabet[i];

    /// <summary>
    /// Returns a rank strictly between <paramref name="prev"/> and <paramref name="next"/>, made by <paramref name="source"/>.
    /// If <paramref name="prev"/> is null, get a rank below <paramref name="next"/>.
    /// If <paramref name="next"/> is null, get a rank above <paramref name="prev"/>.
    /// </summary>
    public static string New(string prev, string next, byte source) =>
        // Added characters never change the order.
        // If ABC < ABD < ABE, then the same is true for any extension of ABD...
        Between(prev, next) + Chr(source + 1);

    private static string Between(string prev, string next)
    {
        prev ??= string.Empty;
        next ??= string.Empty;

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

using System.Collections.Generic;
using System.Text;

/// <summary>
/// "LexoRank" ordering keys for dataset rows.
///
/// If given two strings, it finds the string that is between them in "Ordinal" order:
/// ABCD + ABEFG = ABD
///
/// This ensures that you can always insert a row in-between two other rows if you need to.
/// </summary>
public static class RowRank
{
    // these are in Ordinal order
    private const string Alphabet =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Min_Digit = 0;
    private const int Max_Digit = 62;

    public static readonly IComparer<string> Comparer = System.StringComparer.Ordinal;

    private static int Ord(char c) => Alphabet.IndexOf(c);

    private static char Chr(int i) => Alphabet[i];

    /// <summary>
    /// Returns a rank strictly between <paramref name="prev"/> and <paramref name="next"/>.
    /// If <paramref name="prev"/> is null, get a rank below <paramref name="next"/>.
    /// If <paramref name="next"/> is null, get a rank above <paramref name="prev"/>.
    /// </summary>
    public static string Between(string prev, string next)
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

using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

public class SequenceExpressionsTests
{
    [Theory]
    [InlineData("", "[]")]
    [InlineData("  ", "[]")]
    [InlineData("3", "[3]")]
    [InlineData("1-3,5,7", "[1, 2, 3, 5, 7]")]
    [InlineData(" 1 , 2,,3 ", "[1, 2, 3]")]
    [InlineData("2-3, 1-2", "[2, 3, 1, 2]")]
    [InlineData("3-1", "[3, 2, 1]")]
    [InlineData("1 - 3", "[1, 2, 3]")]
    [InlineData("1*3", "[1, 1, 1]")]
    [InlineData("1-2*2", "[1, 2, 1, 2]")]
    [InlineData("(1, 3-4)*2", "[1, 3, 4, 1, 3, 4]")]
    [InlineData("2*0", "[]")]
    [InlineData("2-7/2", "[2, 4, 6]")]
    [InlineData("(1, 3-6, 8-10)/3", "[1, 5, 9]")]
    [InlineData("(1,3,5)/1", "[1, 3, 5]")]
    [InlineData("1-3*3/2", "[1, 3, 2, 1, 3]")]
    [InlineData("1-3/2*3", "[1, 3, 1, 3, 1, 3]")]
    [InlineData("1,3*2", "[1, 3, 3]")]
    [InlineData("((1,2)*2, 3)/2", "[1, 1, 3]")]
    public void Parses(string input, string expected) => Check(input, null, true, expected);

    [Theory]
    [InlineData(",1,,2,", "[1, 2]")]
    [InlineData("(,1,,2,)*2", "[1, 2, 1, 2]")]
    [InlineData("3-*,", "[]")]
    public void IgnoresExtraCommas(string input, string expected) =>
        Check(input, null, true, expected);

    [Theory]
    [InlineData("3*5", "[3, 3, 3, 3, 3]")]
    [InlineData("3 *2", "[3, 3]")]
    [InlineData("3*, 5", "[3, 5]")]
    public void TheNumberMustTouchItsOperator(string input, string expected) =>
        Check(input, null, true, expected);

    [Theory]
    [InlineData("1-3*", "[1, 2, 3]")]
    [InlineData("3-*", "[]")]
    [InlineData("1, 3-*, 9", "[1, 9]")]
    public void WithoutACountUnboundedRepeatsOnceAndOpenRangesAreEmpty(
        string input,
        string expected
    ) => Check(input, null, true, expected);

    [Theory]
    [InlineData("1-2", 5, "[1, 2, 1, 2, 1]")]
    [InlineData("1-5", 3, "[1, 2, 3]")]
    [InlineData("", 3, "[]")]
    [InlineData("1-5", 0, "[]")]
    [InlineData("1, 3*, 5", 5, "[1, 3, 3, 3, 5]")]
    [InlineData("1, 3-100*, 2", 5, "[1, 3, 4, 5, 2]")]
    [InlineData("1, 3-*", 5, "[1, 3, 4, 5, 6]")]
    [InlineData("(1,2)*, 9", 6, "[1, 2, 1, 2, 1, 9]")]
    [InlineData("1, 3-5*, 7", 2, "[1, 7]")]
    [InlineData("1, 3-5*, 7", 1, "[1]")]
    [InlineData("1, 3-*, 7", 2, "[1, 7]")]
    [InlineData("2*0*, 1", 3, "[1, 1, 1]")]
    public void FitsTheCount(string input, int count, string expected) =>
        Check(input, count, true, expected);

    [Theory]
    [InlineData("1,3-", "[1]")]
    [InlineData("1-", "[]")]
    [InlineData("-2", "[]")]
    [InlineData("3-,5", "[]")]
    [InlineData("1, 3-, 5", "[1]")]
    [InlineData("1, -3", "[1]")]
    [InlineData("1, 3,*2", "[1, 3]")]
    [InlineData("1, (,)", "[1]")]
    [InlineData("3/ 5", "[]")]
    [InlineData("3* 5", "[]")]
    [InlineData("1-3 * 2", "[]")]
    [InlineData("1, 2 3", "[1]")]
    [InlineData("1, 3/0", "[1]")]
    [InlineData("1, 3*, 4*", "[1, 3]")]
    [InlineData("1, 3-*, 4-*", "[1]")]
    [InlineData("1, (2-*)", "[1]")]
    [InlineData("1, (2*)", "[1]")]
    [InlineData("1, 2-*/2", "[1]")]
    [InlineData("1, 2-*2", "[1]")]
    [InlineData("1, 2**3", "[1]")]
    [InlineData("1, ()", "[1]")]
    [InlineData("1, (2", "[1]")]
    [InlineData("1, 2)", "[1]")]
    [InlineData("1, x, 2", "[1]")]
    [InlineData("1, 99999999999", "[1]")]
    [InlineData("1, 2*99999999999", "[1]")]
    public void FailsWithTheExpressionsBeforeTheError(string input, string expected) =>
        Check(input, null, false, expected);

    [Theory]
    [InlineData("1-3, J, Q, K", "[1, 2, 3, J, Q, K]")]
    [InlineData("A-E", "[A, B, C, D, E]")]
    [InlineData("C-A", "[C, B, A]")]
    [InlineData("a-c", "[a, b, c]")]
    [InlineData("a-C, Z-a", "[a-C, Z-a]")]
    [InlineData("(J, Q)*2", "[J, Q, J, Q]")]
    [InlineData(" Draw two , Skip ", "[Draw two, Skip]")]
    [InlineData("Skip (2), +2, J2, 1 2", "[Skip (2), +2, J2, 1 2]")]
    [InlineData("AB-CD, 1-", "[AB-CD, 1-]")]
    [InlineData("007, 1/2, 5*", "[007, 1, 5]")]
    [InlineData("\"Draw, then discard\", \"1-5\"", "[Draw, then discard, 1-5]")]
    [InlineData("(\"a, b\")*2", "[a, b, a, b]")]
    public void ParsesText(string input, string expected) => CheckText(input, true, expected);

    [Theory]
    [InlineData("A, \"open", "[A]")]
    [InlineData("A, (B", "[A]")]
    [InlineData("A, B)", "[A]")]
    [InlineData("A, ()", "[A]")]
    public void FailsOnUnclosedQuotesAndParentheses(string input, string expected) =>
        CheckText(input, false, expected);

    [Theory]
    [InlineData("1, 3-", 3, "[1, 1, 1]")]
    [InlineData("1-", 3, "[]")]
    [InlineData("1, 2*, 3*", 4, "[1, 2, 2, 2]")]
    public void FailsAndStillFitsTheCount(string input, int count, string expected) =>
        Check(input, count, false, expected);

    [Theory]
    [InlineData("1-10000", null, true, 10000)]
    [InlineData("1*10000", null, true, 10000)]
    [InlineData("1-*", 10000, true, 10000)]
    [InlineData("1-10001", null, false, 0)]
    [InlineData("10001-1", null, false, 0)]
    [InlineData("1*10001", null, false, 0)]
    [InlineData("(1-5000, 1-5001)", null, false, 0)]
    [InlineData("1-20000/2", null, false, 0)]
    [InlineData("5, 1-9999, 1-2", null, false, 10000)]
    [InlineData("1-*", 20000, false, 10000)]
    [InlineData("1-20000", 5, false, 0)]
    public void StopsAtTheCap(string input, int? count, bool ok, int length)
    {
        var parsed = SequenceExpressions.TryParseNumbers(input, out var output, count);
        Assert.True(
            parsed == ok && output.Length == length,
            $"{Call(input, count)} returned {parsed} with {output.Length} values, expected {ok} with {length}."
        );
    }

    [Theory]
    [InlineData("1-2|A-C", null, true, "[0:1, 0:2, 1:A, 1:B, 1:C]")]
    [InlineData("1-2|A", 6, true, "[0:1, 0:2, 1:A, 0:1, 0:2, 1:A]")]
    [InlineData("1-2|A, B*", 6, true, "[0:1, 0:2, 1:A, 1:B, 1:B, 1:B]")]
    [InlineData("1-*|A", 6, true, "[0:1, 0:2, 0:3, 0:4, 0:5, 1:A]")]
    [InlineData("1-4|A", 3, true, "[0:1, 0:2, 0:3]")]
    [InlineData("1*|A*|B", 4, false, "[0:1, 0:1, 0:1, 0:1]")]
    [InlineData("1, (|A", null, false, "[0:1]")]
    public void ParsesSeveralInputsAsOne(string inputs, int? count, bool ok, string expected)
    {
        var parsed = SequenceExpressions.TryParseStrings(inputs.Split('|'), out var output, count);
        var actual = $"[{string.Join(", ", output.Select(v => $"{v.Input}:{v.Value}"))}]";
        Assert.True(
            parsed == ok && actual == expected,
            $"Parsing \"{inputs}\" as one{(count is int c ? $" with count {c}" : "")} returned {parsed} with "
                + $"{actual}, expected {ok} with {expected}."
        );
    }

    [Theory]
    [InlineData("1-3|A", 4, false, true)]
    [InlineData("1-3*|A", 4, true, true)]
    [InlineData("1, 3-*|A", 3, true, true)]
    [InlineData("\"\"|", 1, false, true)]
    [InlineData("1-3*|A*", 3, true, false)]
    public void MeasuresTheFewestValues(string inputs, int fewest, bool unbounded, bool ok)
    {
        var actual = SequenceExpressions.Measure(inputs.Split('|'));
        Assert.True(
            actual == (fewest, unbounded, ok),
            $"Measuring \"{inputs}\" gave {actual}, expected ({fewest}, {unbounded}, {ok})."
        );
    }

    [StackTraceHidden]
    private static void Check(string input, int? count, bool ok, string expected)
    {
        var parsed = SequenceExpressions.TryParseNumbers(input, out var output, count);
        var actual = $"[{string.Join(", ", output)}]";
        Assert.True(
            parsed == ok && actual == expected,
            $"{Call(input, count)} returned {parsed} with {actual}, expected {ok} with {expected}."
        );
    }

    [StackTraceHidden]
    private static void CheckText(string input, bool ok, string expected)
    {
        var parsed = SequenceExpressions.TryParseStrings(input, out var output);
        var actual = $"[{string.Join(", ", output)}]";
        Assert.True(
            parsed == ok && actual == expected,
            $"SequenceExpressions.TryParse(\"{input}\") returned {parsed} with {actual}, expected {ok} with {expected}."
        );
    }

    private static string Call(string input, int? count) =>
        $"SequenceExpressions.TryParseNumbers(\"{input}\"{(count is int c ? $", count: {c}" : "")})";
}

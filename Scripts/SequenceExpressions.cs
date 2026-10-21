using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Lizzie;

/// <summary>
/// Parses Sequence Expressions, a microlanguage for lists of values like <c>1-3, J*2, (Q, K)/2, "Draw, then discard"</c>.
/// SequenceExpressions.md describes the language.
/// </summary>
public static class SequenceExpressions
{
    public const int MaxValues = 10_000;

    private enum Kind
    {
        Number,
        Text,
        Quoted,
        Dash,
        Times,
        Every,
        Star,
        Open,
        Close,
        Comma,
    }

    private enum Unbounded
    {
        No,
        Repeat,
        Range,
    }

    /// <summary>
    /// One top-level item. An unbounded <c>s*</c> holds <c>s</c>, and an open range <c>a-*</c> holds <c>[a]</c>.
    /// </summary>
    private record Item(List<string> Values, Unbounded Unbounded);

    private const string Operators = "-*/(),\"";

    private static readonly Tokenizer<Kind> Tokenizer = new TokenizerBuilder<Kind>()
        .Ignore(Span.WhiteSpace)
        .Match(Numerics.Natural, Kind.Number)
        .Match(
            Character
                .Matching(
                    c => !char.IsWhiteSpace(c) && !char.IsDigit(c) && !Operators.Contains(c),
                    "text"
                )
                .AtLeastOnce(),
            Kind.Text
        )
        // An unclosed quote takes the rest of the input, and is an error.
        .Match(
            Character
                .EqualTo('"')
                .IgnoreThen(Character.Except('"').Many())
                .IgnoreThen(Character.EqualTo('"').Optional()),
            Kind.Quoted
        )
        .Match(Character.EqualTo('-'), Kind.Dash)
        .Match(Character.EqualTo('*').IgnoreThen(Numerics.Natural), Kind.Times)
        .Match(Character.EqualTo('*'), Kind.Star)
        .Match(Character.EqualTo('/').IgnoreThen(Numerics.Natural), Kind.Every)
        .Match(Character.EqualTo('('), Kind.Open)
        .Match(Character.EqualTo(')'), Kind.Close)
        .Match(Character.EqualTo(','), Kind.Comma)
        .Match(Character.AnyChar, Kind.Text)
        .Build();

    /// <summary>
    /// Parses the <paramref name="input"/> Sequence Expression(s) into the <paramref name="output"/> list of values,
    /// returning false on an error.
    /// </summary>
    /// <param name="input">The Sequence Expression: <c>1-3, J*2, "Draw, then discard"</c></param>
    /// <param name="output">
    /// The resulting values: <c>[1, 2, 3, J, J, Draw, then discard]</c>. On an error, the values before it.
    /// </param>
    /// <param name="count">
    /// Repeats or trims the list to a length.
    /// When included, the unbounded expression grows or shrinks to fit.
    /// </param>
    /// <returns>Whether or not the parse completed without errors.</returns>
    public static bool TryParseStrings(string input, out string[] output, int? count = null)
    {
        var ok = TryParseStrings([input], out var values, count);
        output = values.Select(v => v.Value).ToArray();
        return ok;
    }

    /// <inheritdoc cref="TryParseStrings(string, out string[], int?)"/>
    /// <param name="inputs">The Sequence Expressions: <c>1-3, J*2, "Draw, then discard"</c></param>
    public static bool TryParseStrings(
        IReadOnlyList<string> inputs,
        out (int Input, string Value)[] output,
        int? count = null
    )
    {
        var ok = Parse(inputs, count, false, out var values);
        output = values.ToArray();
        return ok;
    }

    /// <summary>
    /// Like <see cref="TryParseStrings(string, out string[], int?)"/>, but every value must be a whole number:
    /// a value that isn't one is an error.
    /// </summary>
    public static bool TryParseNumbers(string input, out int[] output, int? count = null)
    {
        var ok = Parse([input], count, true, out var values);
        output = values.Select(v => int.Parse(v.Value)).ToArray();
        return ok;
    }

    /// <summary>
    /// The fewest values the <paramref name="inputs"/> make as one sequence.
    /// </summary>
    /// <remarks>
    /// This is currently just used for the dice faces.
    /// </remarks>
    public static (int Fewest, bool Unbounded, bool Ok) Measure(IReadOnlyList<string> inputs)
    {
        var items = Items(inputs, false, out var ok);
        return (
            items.Sum(i => i.Item.Values.Count),
            items.Any(i => i.Item.Unbounded != Unbounded.No),
            ok
        );
    }

    private static bool Parse(
        IReadOnlyList<string> inputs,
        int? count,
        bool numbers,
        out List<(int Input, string Value)> values
    )
    {
        var items = Items(inputs, numbers, out var ok);
        if (count > MaxValues)
        {
            count = MaxValues;
            ok = false;
        }
        values = Fit(items, count);
        return ok;
    }

    /// <summary>
    /// The top-level items of the <paramref name="inputs"/>, each with the index of its input, up to the first error.
    /// </summary>
    private static List<(int Input, Item Item)> Items(
        IReadOnlyList<string> inputs,
        bool numbers,
        out bool ok
    )
    {
        var items = new List<(int Input, Item Item)>();
        ok = true;
        for (int input = 0; input < inputs.Count && ok; input++)
        {
            var text = inputs[input] ?? "";
            var own = new List<Item>();
            ok = new Reader(text, Tokenizer.Tokenize(text).ToArray()).List(0, own, true);
            int error = own.FindIndex(i =>
                numbers && !i.Values.All(IsNumber)
                || i.Unbounded != Unbounded.No && items.Any(o => o.Item.Unbounded != Unbounded.No)
            );
            if (error >= 0)
            {
                own.RemoveRange(error, own.Count - error);
                ok = false;
            }
            items.AddRange(own.Select(item => (input, item)));
        }
        return items;
    }

    private static bool IsNumber(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// Reads the tokens of one input. Each item between commas is an expression if all of its tokens
    /// make one; otherwise it's text, taken from the input as written.
    /// </summary>
    private sealed class Reader(string input, Token<Kind>[] tokens)
    {
        /// <summary>
        /// Reads items from <paramref name="from"/> up to the end, or up to the <c>)</c> that closes a group,
        /// stopping at the first error.
        /// </summary>
        public bool List(int from, List<Item> items, bool top)
        {
            int to = top ? tokens.Length : Closing(from - 1);
            long total = 0;
            for (int start = from; start < to; )
            {
                int end = ItemEnd(start, to);
                if (end < 0)
                    return false;
                if (end > start)
                {
                    var item = Expression(start, end, top) ?? Text(start, end);
                    if (item?.Values == null)
                        return false;
                    if (
                        item.Unbounded != Unbounded.No
                        && items.Any(i => i.Unbounded != Unbounded.No)
                    )
                        return false;
                    if (item.Unbounded != Unbounded.Range)
                        total += item.Values.Count;
                    if (total > MaxValues)
                        return false;
                    items.Add(item);
                }
                start = end + 1;
            }
            return true;
        }

        /// <summary>
        /// The comma ending the item at <paramref name="start"/>, or <paramref name="to"/> at the end.
        /// Commas inside parentheses don't count. Unbalanced parentheses give -1.
        /// </summary>
        private int ItemEnd(int start, int to)
        {
            int depth = 0;
            for (int i = start; i < to; i++)
            {
                if (Is(i, Kind.Open))
                    depth++;
                else if (Is(i, Kind.Close) && --depth < 0)
                    return -1;
                else if (Is(i, Kind.Comma) && depth == 0)
                    return i;
            }
            return depth == 0 ? to : -1;
        }

        /// <summary>The <c>)</c> matching the <c>(</c> at <paramref name="open"/>, or -1.</summary>
        private int Closing(int open)
        {
            int depth = 0;
            for (int i = open; i < tokens.Length; i++)
            {
                if (Is(i, Kind.Open))
                    depth++;
                else if (Is(i, Kind.Close) && --depth == 0)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The item in tokens <paramref name="start"/> to <paramref name="end"/> as an expression,
        /// null when it isn't one, or an item with null values when it's an expression with an error.
        /// </summary>
        private Item Expression(int start, int end, bool top)
        {
            if (
                top
                && end - start == 3
                && Is(start, Kind.Number)
                && Is(start + 1, Kind.Dash)
                && Is(start + 2, Kind.Star)
            )
                return new Item(Number(start) is int n ? [n.ToString()] : null, Unbounded.Range);

            int at = start;
            if (!Atom(ref at, out var values))
                return null;
            for (; at < end && (Is(at, Kind.Times) || Is(at, Kind.Every)); at++)
            {
                var n = Number(at);
                values = Is(at, Kind.Times) ? Repeat(values, n) : Every(values, n);
            }
            var unbounded = Unbounded.No;
            if (top && at < end && Is(at, Kind.Star))
            {
                unbounded = Unbounded.Repeat;
                at++;
            }
            return at == end ? new Item(values, unbounded) : null;
        }

        /// <summary>
        /// Reads a number, a range, a quote, a word or a group at <paramref name="at"/>.
        /// Returns false when there's none; <paramref name="values"/> is null on an error.
        /// </summary>
        private bool Atom(ref int at, out List<string> values)
        {
            values = null;
            if (Is(at, Kind.Number) && Is(at + 1, Kind.Dash) && Is(at + 2, Kind.Number))
            {
                values = Range(Number(at), Number(at + 2))?.Select(n => n.ToString()).ToList();
                at += 3;
            }
            else if (Is(at, Kind.Text) && Is(at + 1, Kind.Dash) && Is(at + 2, Kind.Text))
            {
                if (
                    Letter(at) is not char a
                    || Letter(at + 2) is not char b
                    || char.IsUpper(a) != char.IsUpper(b)
                )
                    return false;
                values = Range(a, b)?.Select(c => ((char)c).ToString()).ToList();
                at += 3;
            }
            else if (Is(at, Kind.Number) || Is(at, Kind.Text))
                values = [tokens[at++].ToStringValue()];
            else if (Is(at, Kind.Quoted))
            {
                if (Closed(at))
                    values = [tokens[at].ToStringValue()[1..^1]];
                at++;
            }
            else if (Is(at, Kind.Open))
            {
                // A group is a list of its own; an empty one is an error.
                var items = new List<Item>();
                if (List(at + 1, items, false) && items.Count > 0)
                    values = items.SelectMany(i => i.Values).ToList();
                at = Closing(at) + 1;
            }
            else
                return false;
            return true;
        }

        /// <summary>The item as text, or an item with null values when it holds an unclosed quote.</summary>
        private Item Text(int start, int end)
        {
            for (int i = start; i < end; i++)
                if (Is(i, Kind.Quoted) && !Closed(i))
                    return new Item(null, Unbounded.No);
            int from = tokens[start].Span.Position.Absolute;
            var last = tokens[end - 1].Span;
            return new Item(
                [input[from..(last.Position.Absolute + last.Length)].Trim()],
                Unbounded.No
            );
        }

        private bool Is(int i, Kind kind) => i < tokens.Length && tokens[i].Kind == kind;

        private bool Closed(int quote) =>
            tokens[quote].ToStringValue() is { Length: >= 2 } q && q.EndsWith('"');

        /// <summary>The number in the token, after an operator character if it has one.</summary>
        private int? Number(int i)
        {
            var text = tokens[i].ToStringValue();
            return int.TryParse(Is(i, Kind.Number) ? text : text[1..], out var n) ? n : null;
        }

        private char? Letter(int i) =>
            tokens[i].ToStringValue() is [var c] && char.IsAsciiLetter(c) ? c : null;
    }

    private static List<int> Range(int? from, int? to)
    {
        if (from is not int a || to is not int b || Math.Abs((long)b - a) >= MaxValues)
            return null;
        int step = b < a ? -1 : 1;
        return Enumerable.Range(0, Math.Abs(b - a) + 1).Select(i => a + i * step).ToList();
    }

    private static List<string> Repeat(List<string> s, int? times) =>
        s == null || times is not int n || (long)s.Count * n > MaxValues
            ? null
            : Enumerable.Repeat(s, n).SelectMany(p => p).ToList();

    private static List<string> Every(List<string> s, int? step) =>
        s == null || step is not int n || n == 0 ? null : s.Where((_, i) => i % n == 0).ToList();

    private static List<(int Input, string Value)> Fit(
        List<(int Input, Item Item)> items,
        int? count
    )
    {
        // How many values the unbounded expression gets: whatever the bounded ones leave of the count.
        var bounded = items
            .Where(i => i.Item.Unbounded == Unbounded.No)
            .Sum(i => i.Item.Values.Count);
        var room = Math.Max((count ?? 0) - bounded, 0);
        var values = items.SelectMany(i =>
            (
                i.Item.Unbounded switch
                {
                    Unbounded.No => i.Item.Values,
                    Unbounded.Repeat => count == null ? i.Item.Values : Cycle(i.Item.Values, room),
                    _ => Enumerable
                        .Range(0, room)
                        .Select(n => (int.Parse(i.Item.Values[0]) + n).ToString()),
                }
            ).Select(value => (i.Input, value))
        );
        return count is int c ? Cycle(values.ToList(), c).ToList() : values.ToList();
    }

    private static IEnumerable<T> Cycle<T>(List<T> values, int count) =>
        values.Count == 0 ? [] : Enumerable.Range(0, count).Select(i => values[i % values.Count]);
}

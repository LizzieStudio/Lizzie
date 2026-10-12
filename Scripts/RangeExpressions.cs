using System;
using System.Collections.Generic;
using System.Linq;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

/// <summary>
/// Parses Range Expressions, a microlanguage for expressing sequences of integers, like <c>1-3, 5*2, (7-12)/2</c>.
/// RangeExpressions.md describes the language.
/// </summary>
public static class RangeExpressions
{
    public const int MaxValues = 10_000;

    private enum Kind
    {
        Number,
        Dash,
        Times,
        Every,
        Star,
        Open,
        Close,
        Comma,
        Unknown,
    }

    private enum Unbounded
    {
        No,
        Repeat,
        Range,
    }

    /// <summary>
    /// One top-level expression. An unbounded <c>s*</c> holds <c>s</c>, and an open range <c>a-*</c> holds <c>[a]</c>.
    /// </summary>
    private record Item(List<int> Values, Unbounded Unbounded);

    private static readonly Tokenizer<Kind> Tokenizer = new TokenizerBuilder<Kind>()
        .Ignore(Span.WhiteSpace)
        .Match(Numerics.Natural, Kind.Number)
        .Match(Character.EqualTo('-'), Kind.Dash)
        .Match(Character.EqualTo('*').IgnoreThen(Numerics.Natural), Kind.Times)
        .Match(Character.EqualTo('*'), Kind.Star)
        .Match(Character.EqualTo('/').IgnoreThen(Numerics.Natural), Kind.Every)
        .Match(Character.EqualTo('('), Kind.Open)
        .Match(Character.EqualTo(')'), Kind.Close)
        .Match(Character.EqualTo(','), Kind.Comma)
        .Match(Character.AnyChar, Kind.Unknown)
        .Build();

    private static TokenListParser<Kind, int?> Number(Kind kind, int skip) =>
        Token
            .EqualTo(kind)
            .Select(t => int.TryParse(t.ToStringValue().AsSpan(skip), out var n) ? n : (int?)null);

    private static readonly TokenListParser<Kind, int?> Natural = Number(Kind.Number, 0);

    private static readonly TokenListParser<Kind, List<int>> NumberOrRange = Natural.Then(from =>
        Token
            .EqualTo(Kind.Dash)
            .IgnoreThen(Natural)
            .Select(to => Range(from, to))
            .Or(Parse.Return<Kind, List<int>>(from is int n ? [n] : null))
    );

    private static readonly TokenListParser<Kind, Token<Kind>[]> Commas = Token
        .EqualTo(Kind.Comma)
        .Many();

    private static readonly TokenListParser<Kind, List<int>> Group = Token
        .EqualTo(Kind.Open)
        .IgnoreThen(Commas)
        .IgnoreThen(Parse.Ref(() => Bounded).Then(values => Commas.Value(values)).AtLeastOnce())
        .Then(parts => Token.EqualTo(Kind.Close).Value(Concat(parts)));

    private static readonly TokenListParser<Kind, Func<List<int>, List<int>>> Postfix = Number(
            Kind.Times,
            1
        )
        .Select(times => (Func<List<int>, List<int>>)(s => Repeat(s, times)))
        .Or(
            Number(Kind.Every, 1).Select(step => (Func<List<int>, List<int>>)(s => Every(s, step)))
        );

    private static readonly TokenListParser<Kind, List<int>> Bounded =
        from values in NumberOrRange.Or(Group)
        from postfixes in Postfix.Many()
        select postfixes.Aggregate(values, (s, postfix) => postfix(s));

    private static readonly TokenListParser<Kind, Item> OpenRange =
        from start in Natural
        from dash in Token.EqualTo(Kind.Dash)
        from star in Token.EqualTo(Kind.Star)
        select new Item(start is int n ? [n] : null, Unbounded.Range);

    private static readonly TokenListParser<Kind, Item> TopLevel = OpenRange
        .Try()
        .Or(
            from values in Bounded
            from star in Token
                .EqualTo(Kind.Star)
                .Value(Unbounded.Repeat)
                .OptionalOrDefault(Unbounded.No)
            select new Item(values, star)
        )
        .Then(item => Commas.Value(item));

    private static List<int> Range(int? from, int? to)
    {
        if (from is not int a || to is not int b || Math.Abs((long)b - a) >= MaxValues)
            return null;
        int step = b < a ? -1 : 1;
        return Enumerable.Range(0, Math.Abs(b - a) + 1).Select(i => a + i * step).ToList();
    }

    private static List<int> Concat(List<int>[] parts) =>
        parts.Any(p => p == null) || parts.Sum(p => (long)p.Count) > MaxValues
            ? null
            : parts.SelectMany(p => p).ToList();

    private static List<int> Repeat(List<int> s, int? times) =>
        s == null || times is not int n || (long)s.Count * n > MaxValues
            ? null
            : Enumerable.Repeat(s, n).SelectMany(p => p).ToList();

    private static List<int> Every(List<int> s, int? step) =>
        s == null || step is not int n || n == 0 ? null : s.Where((_, i) => i % n == 0).ToList();

    /// <summary>
    /// Parses the <paramref name="input"/> Range Expression into the <paramref name="output"/> sequence of integers,
    /// returning false on an error.
    /// </summary>
    /// <param name="input">The Ranged Expression: <c>1-3, 5*2, (7-12)/2</c></param>
    /// <param name="output">The resulting sequence of integers: <c>[1, 2, 3, 5, 5, 7, 9, 11]</c></param>
    /// <param name="count">
    /// Repeats or trims the sequence to a length.
    /// When included, the unbounded expression grows or shrinks to fit.
    /// </param>
    /// <returns>Whether or not the parse completed without errors.</returns>
    public static bool TryParse(string input, out int[] output, int? count = null)
    {
        var items = new List<Item>();
        var ok = ParseItems(input ?? "", items);
        if (count > MaxValues)
        {
            count = MaxValues;
            ok = false;
        }
        output = Fit(items, count);
        return ok;
    }

    private static bool ParseItems(string input, List<Item> items)
    {
        var remainder = Commas(Tokenizer.Tokenize(input)).Remainder;
        long total = 0;
        while (!remainder.IsAtEnd)
        {
            var result = TopLevel(remainder);
            if (!result.HasValue || result.Value.Values == null)
                return false;
            var item = result.Value;
            if (item.Unbounded != Unbounded.No && items.Any(i => i.Unbounded != Unbounded.No))
                return false;
            if (item.Unbounded != Unbounded.Range)
                total += item.Values.Count;
            if (total > MaxValues)
                return false;
            items.Add(item);
            remainder = result.Remainder;
        }
        return true;
    }

    private static int[] Fit(List<Item> items, int? count)
    {
        // How many values the unbounded expression gets: whatever the bounded ones leave of the count.
        var bounded = items.Where(i => i.Unbounded == Unbounded.No).Sum(i => i.Values.Count);
        var room = Math.Max((count ?? 0) - bounded, 0);
        var values = items.SelectMany(item =>
            item.Unbounded switch
            {
                Unbounded.No => item.Values,
                Unbounded.Repeat => count == null ? item.Values : Cycle(item.Values, room),
                _ => Enumerable.Range(0, room).Select(i => item.Values[0] + i),
            }
        );
        return count is int c ? Cycle(values.ToList(), c).ToArray() : values.ToArray();
    }

    private static IEnumerable<int> Cycle(List<int> values, int count) =>
        values.Count == 0 ? [] : Enumerable.Range(0, count).Select(i => values[i % values.Count]);
}

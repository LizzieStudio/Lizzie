using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Xunit;

/// <summary>
/// Every type that's saved or sent under a polymorphic base, like every <see cref="Replicated"/> record,
/// needs a <see cref="JsonNameAttribute"/> of its own.
/// What the names are, and that they never change, is checked by <see cref="SaveFormatTests"/>.
/// </summary>
public class JsonNameTests
{
    private static readonly Assembly Game = typeof(Replicated).Assembly;

    private static IEnumerable<Type> Classes =>
        Game.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters);

    private static IEnumerable<Type> PolymorphicBases =>
        Game.GetTypes().Where(t => t.IsDefined(typeof(JsonPolymorphicAttribute), false));

    private static string NameOf(Type type) => type.GetCustomAttribute<JsonNameAttribute>()?.Name;

    /// <summary>Fails with every problem, a blank line apart, each a headline followed by the fix.</summary>
    private static void FailWith(IReadOnlyCollection<string> problems)
    {
        if (problems.Count > 0)
            Assert.Fail(string.Join("\n\n", problems));
    }

    /// <summary>The same check the game runs at startup.</summary>
    [Fact]
    public void EveryTypeIsNamed()
    {
        FailWith(JsonNameAttribute.Unnamed().ToList());
    }

    /// <summary>
    /// A name used twice under one base makes System.Text.Json reject the whole base,
    /// so nothing under it could be saved or sent.
    /// </summary>
    [Fact]
    public void NoNameIsUsedTwice()
    {
        FailWith(
            (
                from baseType in PolymorphicBases
                from named in Classes
                    .Where(t => t.IsSubclassOf(baseType) && NameOf(t) != null)
                    .GroupBy(NameOf)
                where named.Count() > 1
                select $"\"{named.Key}\" is the [JsonName] of {string.Join(" and ", named.Select(t => t.Name))}, which are all {baseType.Name}s.\n"
                    + "Each needs its own name. Keep the name on the type that saves already use it for, and give the others their class names."
            ).ToList()
        );
    }

    /// <summary>A name does nothing on a type that isn't under a polymorphic base.</summary>
    [Fact]
    public void EveryNameIsUsed()
    {
        FailWith(
            Classes
                .Where(t => NameOf(t) != null && !PolymorphicBases.Any(t.IsSubclassOf))
                .Select(t =>
                    $"{t.Name} has a [JsonName], but nothing it derives from is [JsonPolymorphic], so the name is never used.\n"
                    + "Remove it, or mark the base type it's saved as with [JsonPolymorphic]."
                )
                .ToList()
        );
    }
}

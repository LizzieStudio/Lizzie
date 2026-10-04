using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Xunit;

/// <summary>
/// Every type that's saved or sent needs a [JsonDerivedType] line on its polymorphic base, each with its own name.
/// What the names are, and that they never change, is checked by <see cref="SaveFormatTests"/>.
/// </summary>
public class JsonDerivedTypeTests
{
    private static readonly Assembly Game = typeof(Replicated).Assembly;

    private static IEnumerable<Type> Concrete =>
        Game.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters);

    private static IEnumerable<Type> PolymorphicBases =>
        Game.GetTypes().Where(t => t.IsDefined(typeof(JsonPolymorphicAttribute), false));

    private static JsonDerivedTypeAttribute[] Listed(Type baseType) =>
        baseType.GetCustomAttributes<JsonDerivedTypeAttribute>(false).ToArray();

    /// <summary>
    /// Every type that must be in a base's list: each class under a polymorphic base,
    /// like every record under <see cref="Replicated"/>.
    /// </summary>
    private static IEnumerable<(Type Base, Type Type)> Required() =>
        from baseType in PolymorphicBases
        from type in Concrete
        where type.IsSubclassOf(baseType) && !type.IsGenericType
        select (baseType, type);

    /// <summary>Fails with every problem, a blank line apart, each a headline followed by the fix.</summary>
    private static void FailWith(IReadOnlyCollection<string> problems)
    {
        if (problems.Count > 0)
            Assert.Fail(string.Join("\n\n", problems));
    }

    [Fact]
    public void EveryTypeIsListed()
    {
        FailWith(
            Required()
                .Where(r => !Listed(r.Base).Any(a => a.DerivedType == r.Type))
                .Select(r =>
                    $"{r.Type.Name} is missing from {r.Base.Name}'s [JsonDerivedType] list, so it can't be saved or sent.\n"
                    + $"Add this above {r.Base.Name}:\n"
                    + $"    [JsonDerivedType(typeof({r.Type.Name}), \"{r.Type.Name}\")]"
                )
                .ToList()
        );
    }

    /// <summary>
    /// A name or type listed twice makes System.Text.Json reject the whole base,
    /// so nothing under it could be saved or sent.
    /// </summary>
    [Fact]
    public void NoNameOrTypeIsListedTwice()
    {
        var problems = new List<string>();
        foreach (var baseType in PolymorphicBases)
        {
            var listed = Listed(baseType);
            problems.AddRange(
                listed
                    .GroupBy(a => a.TypeDiscriminator)
                    .Where(g => g.Count() > 1)
                    .Select(g =>
                        $"\"{g.Key}\" is used by {string.Join(" and ", g.Select(a => a.DerivedType.Name))} in {baseType.Name}'s [JsonDerivedType] list.\n"
                        + "Each needs its own name. Keep the name on the type that saves already use it for, and give the other its class name."
                    )
            );
            problems.AddRange(
                listed
                    .GroupBy(a => a.DerivedType)
                    .Where(g => g.Count() > 1)
                    .Select(g =>
                        $"{g.Key.Name} is listed {g.Count()} times in {baseType.Name}'s [JsonDerivedType] list.\n"
                        + "Keep only the line with the name saves already use."
                    )
            );
        }
        FailWith(problems);
    }
}

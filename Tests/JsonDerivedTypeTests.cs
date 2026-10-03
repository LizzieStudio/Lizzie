using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;
using Xunit;

/// <summary>
/// Every type that's saved or sent needs a [JsonDerivedType] line on its polymorphic base, each with its own name.
/// What the names are, and that they never change, is checked by <see cref="SaveFormatTests"/>.
/// </summary>
public class JsonDerivedTypeTests
{
    private static readonly Assembly Game = typeof(Effect).Assembly;

    private static IEnumerable<Type> Concrete =>
        Game.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters);

    private static IEnumerable<Type> PolymorphicBases =>
        Game.GetTypes().Where(t => t.IsDefined(typeof(JsonPolymorphicAttribute), false));

    private static JsonDerivedTypeAttribute[] Listed(Type baseType) =>
        baseType.GetCustomAttributes<JsonDerivedTypeAttribute>(false).ToArray();

    /// <summary>
    /// Every type that must be in a base's list: the effect that carries each record and value,
    /// and each class under a polymorphic base.
    /// </summary>
    private static IEnumerable<(Type Base, Type Type, Type Derived)> Required()
    {
        foreach (var record in Concrete.Where(t => typeof(IReplicated).IsAssignableFrom(t)))
            yield return (
                typeof(Effect),
                record,
                typeof(UpdateReplicatedEffect<>).MakeGenericType(record)
            );

        // TODO: we should be getting rid of this, once values are records.
        foreach (var effect in TypesNamedInCode().Where(IsValueEffect))
            yield return (typeof(Effect), effect.GenericTypeArguments[0], effect);

        foreach (var baseType in PolymorphicBases)
        foreach (var type in Concrete.Where(t => t.IsSubclassOf(baseType) && !t.IsGenericType))
            yield return (baseType, type, type);
    }

    private static bool IsValueEffect(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SetReplicatedValueEffect<>);

    /// <summary>
    /// Every closed generic type the game's code names, like <c>SetReplicatedValueEffect&lt;ProjectGameSettings&gt;</c>.
    /// Values have no common base, so this is how they're found.
    /// </summary>
    private static List<Type> TypesNamedInCode()
    {
        using var pe = new PEReader(File.OpenRead(Game.Location));
        int rows = pe.GetMetadataReader().GetTableRowCount(TableIndex.TypeSpec);
        var types = new List<Type>();
        for (int row = 1; row <= rows; row++)
        {
            try
            {
                var token = MetadataTokens.GetToken(MetadataTokens.TypeSpecificationHandle(row));
                types.Add(Game.ManifestModule.ResolveType(token));
            }
            catch (ArgumentException)
            {
                // named inside generic code, like UpdateReplicatedEffect<T>, so not closed
            }
        }
        return types;
    }

    private static string CSharpName(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GenericTypeArguments.Select(CSharpName))}>"
            : type.Name;

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
                .Where(r => !Listed(r.Base).Any(a => a.DerivedType == r.Derived))
                .Select(r =>
                    $"{r.Type.Name} is missing from {r.Base.Name}'s [JsonDerivedType] list, so it can't be saved or sent.\n"
                    + $"Add this above {r.Base.Name}:\n"
                    + $"    [JsonDerivedType(typeof({CSharpName(r.Derived)}), \"{r.Type.Name}\")]"
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
                        $"\"{g.Key}\" is used by {string.Join(" and ", g.Select(a => CSharpName(a.DerivedType)))} in {baseType.Name}'s [JsonDerivedType] list.\n"
                        + "Each needs its own name. Keep the name on the type that saves already use it for, and give the other its class name."
                    )
            );
            problems.AddRange(
                listed
                    .GroupBy(a => a.DerivedType)
                    .Where(g => g.Count() > 1)
                    .Select(g =>
                        $"{CSharpName(g.Key)} is listed {g.Count()} times in {baseType.Name}'s [JsonDerivedType] list.\n"
                        + "Keep only the line with the name saves already use."
                    )
            );
        }
        FailWith(problems);
    }
}

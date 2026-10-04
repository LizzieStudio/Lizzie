using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// <para>The name a type is saved and sent under, beneath its <c>[JsonPolymorphic]</c> base, like <see cref="Replicated"/>.</para>
/// <para>
/// Every class under such a base needs one. <see cref="ThrowIfAnyUnnamed"/> reports any that don't at startup,
/// and <c>JsonNameTests</c> in the tests.
/// If you change a JsonName, it will break saves.
/// In general, don't change the JsonName, even if you rename the class.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JsonNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    private static readonly Type[] Named = typeof(JsonNameAttribute)
        .Assembly.GetTypes()
        .Where(t => t.IsDefined(typeof(JsonNameAttribute), false))
        // in name order, so the order doesn't depend on where the types are declared
        .OrderBy(t => t.GetCustomAttribute<JsonNameAttribute>().Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Each class under a <c>[JsonPolymorphic]</c> base without a name, as a message with the line to add.
    /// </summary>
    public static IEnumerable<string> Unnamed() =>
        from type in typeof(JsonNameAttribute).Assembly.GetTypes()
        where type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters
        where !type.IsDefined(typeof(JsonNameAttribute), false)
        from baseType in type.Assembly.GetTypes()
        where
            baseType.IsDefined(typeof(JsonPolymorphicAttribute), false)
            && type.IsSubclassOf(baseType)
        select $"{type.Name} has no [JsonName], so it can't be saved or sent as a {baseType.Name}.\n"
            + $"Add this above {type.Name}:\n"
            + $"    [JsonName(\"{type.Name}\")]";

    /// <summary>
    /// Throws when a class is missing its name, which would otherwise fail the first time it's saved or sent.
    /// Called at startup.
    /// </summary>
    public static void ThrowIfAnyUnnamed()
    {
        var unnamed = Unnamed().ToList();
        if (unnamed.Count > 0)
            throw new InvalidOperationException(string.Join("\n\n", unnamed));
    }

    /// <summary>
    /// A resolver modifier that lists every named type under each polymorphic base it derives from,
    /// in place of <c>[JsonDerivedType]</c> lines on the base.
    /// </summary>
    public static void AddDerivedTypes(JsonTypeInfo info)
    {
        // Only types marked [JsonPolymorphic] have polymorphism options.
        if (info.PolymorphismOptions == null)
            return;
        foreach (var type in Named.Where(t => t.IsSubclassOf(info.Type)))
            info.PolymorphismOptions.DerivedTypes.Add(
                new JsonDerivedType(type, type.GetCustomAttribute<JsonNameAttribute>().Name)
            );
    }
}

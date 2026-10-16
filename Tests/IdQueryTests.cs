using System;
using System.Linq;
using Xunit;

/// <summary>
/// Every atom type declares exactly one <see cref="Id{T, I}"/>, which its store holds atoms by.
/// </summary>
public class IdQueryTests
{
    [Fact]
    public void EveryAtomTypeDeclaresOneId()
    {
        var problems = typeof(Atom)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Atom)) && !t.IsAbstract)
            .Select(t =>
            {
                try
                {
                    IdQuery.For(t);
                    return null;
                }
                catch (InvalidOperationException e)
                {
                    return e.Message;
                }
            })
            .Where(problem => problem != null)
            .ToList();
        if (problems.Count > 0)
            Assert.Fail(string.Join("\n\n", problems));
    }
}

using System;
using System.Linq;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Every atom type declares exactly one <see cref="Identity{T, I}"/>, which its store holds atoms by.
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

    private static SnowTag Tag(int counter) => new(Snowport.HostSource, counter);

    [Fact]
    public void AnEmptyCellIsInTheGridWhileItsRowAndColumnAre()
    {
        var R = new ListReader(new DataRow { Id = Tag(1) }, new DataColumn { Id = Tag(2) });
        Assert.True(DataCell.InGrid(R, new CellId(Tag(1), Tag(2))));
        Assert.False(DataCell.InGrid(R, new CellId(Tag(3), Tag(2))));
        Assert.False(DataCell.InGrid(R, new CellId(Tag(1), Tag(3))));
    }
}

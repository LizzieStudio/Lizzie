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

    private static Ref<T> Tag<T>(int counter)
        where T : Atom => new(new SnowTag(Snowport.HostSource, counter));

    [Fact]
    public void AnEmptyCellIsInTheGridWhileItsRowAndColumnAre()
    {
        var R = new ListReader(
            new DataRow { Id = Tag<DataRow>(1) },
            new DataColumn { Id = Tag<DataColumn>(2) }
        );
        Assert.True(DataCell.InGrid(R, new CellId(Tag<DataRow>(1), Tag<DataColumn>(2))));
        Assert.False(DataCell.InGrid(R, new CellId(Tag<DataRow>(3), Tag<DataColumn>(2))));
        Assert.False(DataCell.InGrid(R, new CellId(Tag<DataRow>(1), Tag<DataColumn>(3))));
    }

    // A cell nobody wrote has a stand-in, but an empty one must not, or commands would act on a cell in no row.
    [Fact]
    public void AnEmptyRefResolvesToNothingEvenWhenItsTypeHasAStandIn()
    {
        var R = new ListReader();
        Assert.True(
            R.Resolve(new CellId(Tag<DataRow>(1), Tag<DataColumn>(2))) is DataCell,
            "A cell nobody wrote didn't resolve to its stand-in."
        );
        Assert.True(
            R.Resolve(new CellId(Ref<DataRow>.Empty, Tag<DataColumn>(2))) == null,
            "A cell with an empty row resolved to a stand-in."
        );
    }
}

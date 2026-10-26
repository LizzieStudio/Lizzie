using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// A reset deletes what a stage has that the stage it builds on doesn't.
/// </summary>
public class GoesOnResetTests
{
    private static readonly Ref<Stage> Blueprint = new(1),
        Table = new(2);
    private static readonly Ref<Component> Card = new(3);

    private static readonly Stage[] Stages =
    [
        new() { Id = Blueprint },
        new()
        {
            Id = Table,
            ParentRef = Blueprint,
            IsTable = true,
        },
    ];

    private static ComponentPosition In(Ref<Stage> stage) => new() { Id = Card, StageRef = stage };

    [Fact]
    public void WhatOnlyTheTableHasGoes() =>
        Assert.True(
            ComponentPosition.GoesOnReset(new ListReader([.. Stages, In(Table)]), new(Table, Card)),
            "A card only the table has stays on reset, expected it to go."
        );

    [Fact]
    public void WhatTheBlueprintHasStays() =>
        Assert.False(
            ComponentPosition.GoesOnReset(
                new ListReader([.. Stages, In(Blueprint), In(Table)]),
                new(Table, Card)
            ),
            "A card the blueprint has goes on reset, expected it to stay."
        );

    [Fact]
    public void NothingInARootGoes() =>
        Assert.False(
            ComponentPosition.GoesOnReset(
                new ListReader([.. Stages, In(Blueprint)]),
                new(Blueprint, Card)
            ),
            "A card in a root stage goes on reset, expected it to stay, since a root has nothing to reset to."
        );
}

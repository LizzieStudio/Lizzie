using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveUpgrades;
using Xunit;

namespace Lizzie.Tests;

public class SaveUpgradeTests
{
    private static List<JsonObject> Lines(params string[] lines) =>
        lines.Select(l => JsonNode.Parse(l).AsObject()).ToList();

    [Fact]
    public void LeavesTheEventsOfACurrentSave()
    {
        var lines = Lines(SaveUpgrade.Header, """{"id":1,"writes":[]}""");
        SaveUpgrade.Run(lines);
        Assert.True(
            JsonNode.DeepEquals(Assert.Single(lines), JsonNode.Parse("""{"id":1,"writes":[]}"""))
        );
    }

    [Fact]
    public void RefusesANewerSave() =>
        Assert.Throws<JsonException>(() =>
            SaveUpgrade.Run(Lines($$"""{"format":{{SaveUpgrade.Current + 1}}}""", """{"id":1}"""))
        );

    [Fact]
    public void RefusesStepsOutOfOrder() =>
        Assert.Throws<InvalidOperationException>(() =>
            SaveUpgrade.InOrder(
                new Addition { Format = 1 },
                new Addition { Format = 3 }
            )
        );
}

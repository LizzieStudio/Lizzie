using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SaveUpgrades;
using Xunit;

namespace Lizzie.Tests;

/// <summary>
/// Release 1's drafts on what the command branch saved, which saves from main never hold.
/// </summary>
public class Release1DraftTests
{
    private static List<JsonObject> Writes(IStep draft, string writes)
    {
        var save = draft.Run(new JsonObject { ["writes"] = JsonNode.Parse(writes) });
        return ((JsonArray)save["writes"]).Cast<JsonObject>().ToList();
    }

    private static List<JsonObject> OfType(List<JsonObject> writes, string type) =>
        writes.Where(w => (string)w["$type"] == type).ToList();

    [Fact]
    public void CardsInADeckGetTheirOwnComponentsFromTheDeck()
    {
        var writes = Writes(
            new ReconciledDecks(),
            """
            [
              { "$type": "Prototype", "id": 1, "parameters": { "$type": "PrintedParameters",
                "front": { "$type": "QuickSide", "suits": [ { "values": "\"Ace\"*2" }, { "values": "\"King\"" } ] } } },
              { "$type": "ComponentVariant", "id": 2, "prototypeRef": 1 },
              { "$type": "ComponentVariant", "id": 3, "prototypeRef": 1,
                "item": { "$type": "PrintedItem", "number": 1, "frontRow": 0, "backRow": 0 } },
              { "$type": "ComponentPosition", "id": 10, "stageRef": 9, "variantRef": 2 },
              { "$type": "ComponentPosition", "id": 11, "stageRef": 9, "variantRef": 3, "containerRef": 10 },
              { "$type": "ComponentPosition", "id": 12, "stageRef": 9, "variantRef": 3, "containerRef": 10 }
            ]
            """
        );

        var variants = OfType(writes, "ComponentVariant");
        Assert.Equal(3, variants.Count);
        Assert.Equal(3, variants.Select(v => (int)v["id"]).Distinct().Count());
        Assert.Equal([null, 10, 10], variants.Select(v => (int?)v["deckRef"]));

        // the second card of "Ace"*2 is still an Ace, in the first suit
        var suit = (int)writes[0]["parameters"]["front"]["suits"][0]["id"];
        var front = variants[1]["item"]["front"];
        Assert.Equal(suit, (int)front["suit"]);
        Assert.Equal("Ace", (string)front["value"]);
    }

    [Fact]
    public void HandsAndCursorsBecomeLocationsOfTheirSeat()
    {
        var writes = Writes(
            new SeatAtoms(),
            """
            [
              { "$type": "ProjectGameSettings", "players": [
                { "name": "Seat 1", "isAdmin": false, "handRef": 50, "cursorRef": 51, "handHoldRef": 52 } ] },
              { "$type": "ComponentPosition", "id": 1, "containerRef": 50 },
              { "$type": "ComponentPosition", "id": 2, "containerRef": 51 },
              { "$type": "ComponentPosition", "id": 3, "containerRef": 52 },
              { "$type": "ComponentPosition", "id": 4, "containerRef": 1 }
            ]
            """
        );

        int seat = (int)Assert.Single(OfType(writes, "Seat"))["id"];
        Assert.DoesNotContain(seat, new[] { 1, 2, 3, 4, 50, 51, 52 });
        var containers = OfType(writes, "ComponentPosition").Select(p => p["container"]);
        Assert.Equal(
            [
                $$"""{"$type":"InHand","seat":{{seat}}}""",
                $$"""{"$type":"Held","seat":{{seat}},"overHand":false}""",
                $$"""{"$type":"Held","seat":{{seat}},"overHand":true}""",
                """{"$type":"InComponent","component":1}""",
            ],
            containers.Select(c => c.ToJsonString())
        );
    }

    [Fact]
    public void ZOrdersBecomeRanksInTheSameStack()
    {
        var writes = Writes(
            new SnowRanks(),
            """
            [
              { "$type": "ComponentPosition", "id": 1, "stageRef": 9, "zOrder": { "target": 1, "suborder": 1, "stamp": 3 } },
              { "$type": "ComponentPosition", "id": 2, "stageRef": 9, "zOrder": { "target": 0, "suborder": 0, "stamp": 5 } },
              { "$type": "ComponentPosition", "id": 3, "stageRef": 9, "zOrder": { "target": 1, "suborder": 0, "stamp": 3 } },
              { "$type": "ComponentPosition", "id": 4, "stageRef": 9, "zOrder": { "target": 0, "suborder": 0, "stamp": 9 } },
              { "$type": "DataRow", "id": 5, "rank": "a+b" }
            ]
            """
        );

        // sent to the bottom, the newest lowest, then sent to the top, by suborder
        var stack = OfType(writes, "ComponentPosition")
            .OrderBy(p => (string)p["rank"]["steps"][0]["place"], System.StringComparer.Ordinal)
            .Select(p => (int)p["id"]);
        Assert.Equal([4, 2, 3, 1], stack);
        Assert.Equal("a*b", (string)OfType(writes, "DataRow")[0]["rank"]["steps"][0]["place"]);
    }

    [Fact]
    public void ABoxedComponentBecomesARemoval()
    {
        var writes = Writes(
            new ComponentLocation(),
            """
            [
              { "$type": "ComponentPosition", "id": 1, "stageRef": 9, "x": 5, "container": { "$type": "InBox" } },
              { "$type": "ComponentPosition", "id": 2, "stageRef": 9, "container": { "$type": "Held", "seat": 7, "overHand": true } }
            ]
            """
        );

        Assert.Equal(
            """{"$type":"ComponentPosition","id":1,"stageRef":9,"removed":true}""",
            writes[0].ToJsonString()
        );
        Assert.Equal("Dragged", (string)writes[1]["location"]["$type"]);
    }

    [Fact]
    public void ASeatAZoneBothIncludedAndExcludedStaysBlocked()
    {
        var writes = Writes(
            new WeakMaps(),
            """
            [ { "$type": "Prototype", "id": 1, "parameters": { "$type": "ZoneParameters",
                "includedSeats": [ 7, 8 ], "excludedSeats": [ 8 ] } } ]
            """
        );

        Assert.Equal("""{"7":1,"8":2}""", writes[0]["parameters"]["seats"].ToJsonString());
    }
}

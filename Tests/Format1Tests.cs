using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SaveUpgrades;
using Xunit;

namespace Lizzie.Tests;

public class Format1Tests
{
    /// <summary>
    /// A main-branch project: a dataset, a template on it, a template deck, and a snapshot
    /// with the deck holding one of its cards.
    /// </summary>
    private const string Project = """
        {
          "Name": "P",
          "Templates": { "Card": { "Name": "Card", "SizeTemplate": "Poker", "Width": 2.5, "Height": 3.5,
            "Elements": [ { "Type": "Text", "Text": "{Name}" } ], "DataSet": "Things" } },
          "Datasets": { "Things": { "Name": "Things", "Columns": [ "Cost" ],
            "Rows": { "Axe": { "Name": "Axe", "Qty": 2, "Data": [ "3" ] },
                      "Bow": { "Name": "Bow", "Qty": 1, "Data": [ "" ] } } } },
          "Prototypes": { "d1": { "PrototypeRef": "d1", "Name": "Deck", "Type": 6,
            "Parameters": { "ComponentName": "Deck", "BaseName": "Printed", "Height": 88.9, "Width": 63.5,
              "Mode": 3, "FrontTemplate": "Card", "BackTemplate": "", "Dataset": "Things" } } },
          "Images": {},
          "GameStates": { "Setup": { "Name": "Setup", "CapturedAt": "2026-01-01T00:00:00Z", "Components": [
            { "ComponentRef": "deck", "PrototypeRef": "d1", "DataSetRow": "", "Px": 1.5, "Pz": -2, "ZOrder": 1,
              "ContainedComponents": [ "card" ] },
            { "ComponentRef": "card", "PrototypeRef": "d1", "DataSetRow": "Bow", "Px": 0, "Pz": 0, "ZOrder": 2,
              "ContainedComponents": [] } ] } },
          "GameSettings": { "TableColorR": 0.5, "TableColorG": 0.5, "TableColorB": 0.5, "TableColorA": 1,
            "Players": [ { "Name": "Seat 1", "ColorR": 1, "ColorG": 0, "ColorB": 0, "ColorA": 1 } ] }
        }
        """;

    private static List<JsonObject> Writes()
    {
        var lines = new List<JsonObject> { JsonNode.Parse(Project).AsObject() };
        SaveUpgrade.Run(lines);
        return ((JsonArray)Assert.Single(lines)["writes"]).Cast<JsonObject>().ToList();
    }

    private static List<JsonObject> OfType(List<JsonObject> writes, string type) =>
        writes.Where(w => (string)w["$type"] == type).ToList();

    [Fact]
    public void RowNamesBecomeAFirstColumn()
    {
        var writes = Writes();
        var columns = OfType(writes, "DataColumn").OrderBy(c => (string)c["rank"]).ToList();
        Assert.Equal(["Name", "Cost"], columns.Select(c => (string)c["name"]));
        var cells = OfType(writes, "DataCell").Select(c => (string)c["value"]).ToList();
        Assert.Equal(["Axe", "3", "Bow"], cells);
    }

    [Fact]
    public void TemplatesAndDecksReferToTheirDataset()
    {
        var writes = Writes();
        var dataset = (int)Assert.Single(OfType(writes, "DataSet"))["id"];
        Assert.Equal(dataset, (int)Assert.Single(OfType(writes, "Template"))["dataSet"]);
        var side = Assert.Single(OfType(writes, "Prototype"))["parameters"]["front"];
        Assert.Equal("TemplateSide", (string)side["$type"]);
        Assert.Equal(dataset, (int)side["dataset"]);
    }

    [Fact]
    public void ASnapshotBecomesTheBlueprintTheTableStartsOn()
    {
        var writes = Writes();
        var stages = OfType(writes, "Stage");
        var stage = Assert.Single(stages, s => s["isTable"] == null);
        Assert.Equal("Setup", (string)stage["name"]);
        var table = Assert.Single(stages, s => s["isTable"] != null);
        Assert.Equal((int)stage["id"], (int)table["parentRef"]);
        Assert.Equal(
            (int)table["id"],
            (int)Assert.Single(OfType(writes, "CurrentTable"))["stageRef"]
        );

        var positions = OfType(writes, "ComponentPosition");
        var deck = positions.Single(p => p["containerRef"] == null);
        var card = positions.Single(p => p["containerRef"] != null);
        Assert.Equal(150, (int)deck["x"]);
        Assert.Equal((int)deck["id"], (int)card["containerRef"]);

        var variants = OfType(writes, "ComponentVariant").ToDictionary(v => (int)v["id"]);
        Assert.Null(variants[(int)deck["variantRef"]]["item"]);
        Assert.Equal((int)deck["id"], (int)variants[(int)card["variantRef"]]["deckRef"]);
        var item = variants[(int)card["variantRef"]]["item"];
        var bow = OfType(writes, "DataRow").OrderBy(r => (string)r["rank"]).Last();
        Assert.Equal("RowFace", (string)item["front"]["$type"]);
        Assert.Equal((int)bow["id"], (int)item["front"]["row"]);
        Assert.Null(item["back"]);
    }

    /// <summary>
    /// A main-branch project with a quick deck of two cards, and a snapshot with its second card.
    /// </summary>
    private const string QuickProject = """
        {
          "Name": "P", "Templates": {}, "Datasets": {}, "Images": {},
          "Prototypes": { "q1": { "PrototypeRef": "q1", "Name": "Quick", "Type": 3,
            "Parameters": { "ComponentName": "Quick", "BaseName": "Printed", "Height": 88.9, "Width": 63.5,
              "Mode": 5, "QuickCardData": [ { "Caption": "Ace" }, { "Caption": "1-3" } ] } } },
          "GameStates": { "Setup": { "Name": "Setup", "CapturedAt": "2026-01-01T00:00:00Z", "Components": [
            { "ComponentRef": "card", "PrototypeRef": "q1", "DataSetRow": "2", "Px": 0, "Pz": 0, "ZOrder": 1,
              "ContainedComponents": [] } ] } },
          "GameSettings": { "TableColorR": 0.5, "TableColorG": 0.5, "TableColorB": 0.5, "TableColorA": 1,
            "Players": [] }
        }
        """;

    [Fact]
    public void AQuickCardShowsItsCaptionInItsOwnSuit()
    {
        var lines = new List<JsonObject> { JsonNode.Parse(QuickProject).AsObject() };
        SaveUpgrade.Run(lines);
        var writes = ((JsonArray)Assert.Single(lines)["writes"]).Cast<JsonObject>().ToList();

        var suits = (
            (JsonArray)Assert.Single(OfType(writes, "Prototype"))["parameters"]["front"]["suits"]
        )
            .Cast<JsonObject>()
            .ToList();
        Assert.Equal(["\"Ace\"", "\"1-3\""], suits.Select(s => (string)s["values"]));
        Assert.NotEqual((int)suits[0]["id"], (int)suits[1]["id"]);

        var front = Assert.Single(OfType(writes, "ComponentVariant"))["item"]["front"];
        Assert.Equal("QuickFace", (string)front["$type"]);
        Assert.Equal((int)suits[1]["id"], (int)front["suit"]);
        Assert.Equal("1-3", (string)front["value"]);
    }
}

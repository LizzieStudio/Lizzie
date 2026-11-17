using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SaveUpgrades;

/// <summary>
/// <para>
/// Decks count the cards they issued, so each component gets its own variant,
/// which names the deck that holds it.
/// </para>
///
/// <para>
/// A printed card names its faces instead of a number and rows, so quick suits get ids for faces to refer to.
/// A quick card shows the value its number picks from its side's suits,
/// reading each suit as a quoted value, maybe repeated like <c>"Ace"*3</c>, or as plain text.
/// </para>
/// </summary>
public sealed class ReconciledDecks : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        var atoms = Edits.GetObjects(writes);
        int next = Edits.NextTag(writes);

        var prototypes = new Dictionary<int, JsonObject>();
        foreach (var prototype in atoms.Where(w => (string)w["$type"] == "Prototype"))
        {
            prototypes[(int)prototype["id"]] = prototype;
            var parameters = (JsonObject)prototype["parameters"];
            foreach (
                var side in new[] { parameters["front"], parameters["back"], parameters["faces"] }
            )
            {
                if ((string)side?["$type"] != "QuickSide")
                    continue;
                foreach (var suit in ((JsonArray)side["suits"]).Cast<JsonObject>())
                    suit.Insert(0, "id", next++);
            }
        }

        var variants = atoms.Where(w => (string)w["$type"] == "ComponentVariant").ToList();
        foreach (var variant in variants)
        {
            if ((string)variant["item"]?["$type"] != "PrintedItem")
                continue;
            var parameters = (JsonObject)prototypes[(int)variant["prototypeRef"]]["parameters"];
            variant["item"] = Item((JsonObject)variant["item"], parameters);
        }

        var byId = variants.ToDictionary(v => (int)v["id"]);
        var positions = atoms.Where(w => (string)w["$type"] == "ComponentPosition").ToList();
        var oldVariants = new Dictionary<int, JsonObject>();
        foreach (var position in positions)
            oldVariants.TryAdd((int)position["id"], byId[(int)position["variantRef"]]);

        var own = new Dictionary<int, int>();
        foreach (var position in positions)
        {
            int component = (int)position["id"];
            if (!own.TryGetValue(component, out int tag))
            {
                tag = own[component] = next++;
                var variant = (JsonObject)oldVariants[component].DeepClone();
                variant["id"] = tag;
                if (
                    position["containerRef"] is { } container
                    && IsDeck(oldVariants[(int)container])
                )
                    variant.Insert(variant.IndexOf("prototypeRef") + 1, "deckRef", (int)container);
                writes.Insert(writes.IndexOf(position), variant);
            }
            position["variantRef"] = tag;
        }

        foreach (var variant in oldVariants.Values.Distinct())
            writes.Remove(variant);

        bool IsDeck(JsonObject variant) =>
            variant["item"] == null
            && (string)prototypes[(int)variant["prototypeRef"]]["parameters"]["$type"]
                == "PrintedParameters";
    }

    /// <summary>
    /// A printed card's item, with the faces its number and rows pick on its prototype's sides.
    /// </summary>
    private static JsonObject Item(JsonObject old, JsonObject parameters)
    {
        int number = (int)old["number"];
        var front = parameters["front"];
        var back = parameters["back"];
        JsonObject frontFace = null,
            backFace = null;
        switch ((string)front["$type"])
        {
            case "QuickSide":
                frontFace = QuickFaces(front).ElementAtOrDefault(number);
                if (back != null)
                    backFace = QuickFaces(back).FirstOrDefault();
                break;
            case "ImageSide":
                frontFace = FrameFace(number + 1);
                if (back != null)
                    backFace = FrameFace((string)back["frames"] == "1" ? 1 : number + 1);
                break;
            case "TemplateSide":
                // row 0 is no row
                int frontRow = (int)old["frontRow"];
                int backRow = (int)old["backRow"];
                if (frontRow != 0)
                    frontFace = RowFace(frontRow);
                if (back != null && backRow != 0)
                    backFace = RowFace(backRow);
                break;
        }

        var item = new JsonObject { ["$type"] = "PrintedItem" };
        if (frontFace != null)
            item["front"] = frontFace;
        if (backFace != null)
            item["back"] = backFace;
        return item;
    }

    /// <summary>
    /// The face of each card a quick side makes, in order.
    /// </summary>
    private static IEnumerable<JsonObject> QuickFaces(JsonNode side)
    {
        foreach (var suit in ((JsonArray)side["suits"]).Cast<JsonObject>())
        {
            string values = (string)suit["values"] ?? "";
            var quoted = Regex.Match(values, @"^""(.*)""(?:\*(\d+))?$");
            string value = quoted.Success ? quoted.Groups[1].Value : values;
            int count = quoted.Groups[2].Success ? int.Parse(quoted.Groups[2].Value) : 1;
            for (int i = 0; i < count; i++)
            {
                yield return new JsonObject
                {
                    ["$type"] = "QuickFace",
                    ["suit"] = (int)suit["id"],
                    ["value"] = value,
                };
            }
        }
    }

    private static JsonObject FrameFace(int frame) =>
        new() { ["$type"] = "FrameFace", ["frame"] = frame };

    private static JsonObject RowFace(int row) => new() { ["$type"] = "RowFace", ["row"] = row };
}

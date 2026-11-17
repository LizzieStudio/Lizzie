using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// <para>
/// A rank is a side and a list of steps, each a stamp and a place, instead of a string.
/// An old rank keeps its order as the place of one step, all sharing one old stamp,
/// with its lowest character <c>+</c> becoming <c>*</c>.
/// </para>
///
/// <para>
/// A position's z-order became a rank among the positions of its stage, in the order the z-orders put them.
/// </para>
/// </summary>
public sealed class SnowRanks : WriteStep
{
    /// <summary>
    /// The stamp of every converted rank, older than any made since.
    /// </summary>
    private const int Stamp = 64;

    protected override void Run(JsonArray writes)
    {
        var positions = new List<JsonObject>();
        foreach (var write in Edits.GetObjects(writes))
        {
            if ((string)write["$type"] == "ComponentPosition")
                positions.Add(write);
            else if (write["rank"] is JsonValue rank)
                write["rank"] = Rank(((string)rank).Replace('+', '*'));
        }

        foreach (var stage in positions.GroupBy(p => (int)p["stageRef"]))
        {
            var ordered = stage.OrderBy(p => (JsonObject)p["zOrder"], new ZOrders()).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                int index = ordered[i].IndexOf("zOrder");
                ordered[i].RemoveAt(index);
                ordered[i].Insert(index, "rank", Rank(Edits.Place(i)));
            }
        }
    }

    private static JsonObject Rank(string place) =>
        new()
        {
            ["side"] = 1,
            ["steps"] = new JsonArray(new JsonObject { ["stamp"] = Stamp, ["place"] = place }),
        };

    /// <summary>
    /// Sorts old z-orders bottom to top.
    /// Those sent to the bottom come first, the newest lowest,
    /// then those sent to the top, the newest highest,
    /// then by suborder for the same target and stamp.
    /// </summary>
    private sealed class ZOrders : IComparer<JsonObject>
    {
        public int Compare(JsonObject a, JsonObject b)
        {
            int target = (int)a["target"];
            if (target != (int)b["target"])
                return target.CompareTo((int)b["target"]);
            // a stamp from a file reads as a long, but one Format1 just wrote holds an int
            int stamps = long.Parse(a["stamp"].ToJsonString())
                .CompareTo(long.Parse(b["stamp"].ToJsonString()));
            if (stamps != 0)
                return target == 1 ? stamps : -stamps;
            return ((int)a["suborder"]).CompareTo((int)b["suborder"]);
        }
    }
}

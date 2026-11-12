using System.Linq;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// <para>
/// A quick side keys its suits by their tags, and lists the tags in order.
/// </para>
///
/// <para>
/// A zone keys its rule for each seat by the seat's tag, 1 to allow it and 2 to block it,
/// instead of listing included and excluded seats.
/// A seat in both lists stays blocked.
/// </para>
/// </summary>
public sealed class WeakMaps : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        foreach (var obj in Edits.GetObjectsRecursive(writes))
        {
            switch ((string)obj["$type"])
            {
                case "QuickSide":
                    var suits = ((JsonArray)obj["suits"]).Cast<JsonObject>().ToList();
                    obj.Remove("suits");
                    obj["order"] = new JsonArray([.. suits.Select(s => s["id"].DeepClone())]);
                    var keyed = new JsonObject();
                    foreach (var suit in suits)
                    {
                        string id = suit["id"].ToJsonString();
                        suit.Remove("id");
                        keyed[id] = suit.DeepClone();
                    }
                    obj["suits"] = keyed;
                    break;
                case "ZoneParameters":
                    var seats = new JsonObject();
                    foreach (
                        var (list, access) in new[] { ("includedSeats", 1), ("excludedSeats", 2) }
                    )
                    {
                        foreach (var seat in (JsonArray)obj[list] ?? [])
                            seats[seat.ToJsonString()] = access;
                    }
                    Edits.Rename(obj, "includedSeats", "seats");
                    obj["seats"] = seats;
                    obj.Remove("excludedSeats");
                    break;
            }
        }
    }
}

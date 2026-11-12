using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// <para>
/// The project settings' players became Seat atoms, ranked in their old order,
/// and zones refer to seats by tag instead of by number.
/// A seat with its default name goes by its number instead.
/// </para>
///
/// <para>
/// A position names its container, which may be a component, a seat's hand, or a seat's held cards,
/// instead of a tag that players' hands and cursors used to share with components.
/// </para>
/// </summary>
public sealed class SeatAtoms : WriteStep
{
    protected override void Run(JsonArray writes)
    {
        var atoms = Edits.GetObjects(writes);
        int next = Edits.NextTag(writes);
        var seats = new Dictionary<int, int>();
        int Seat(int number) =>
            seats.TryGetValue(number, out int tag) ? tag : seats[number] = next++;

        var holders = new Dictionary<int, JsonObject>();
        var settings = atoms.FirstOrDefault(w => (string)w["$type"] == "ProjectGameSettings");
        if (settings != null)
        {
            settings.Remove("allowObservers");
            settings.Remove("maxPlayers");
            var players = (JsonArray)settings["players"] ?? [];
            settings.Remove("players");

            int at = writes.IndexOf(settings);
            for (int i = 0; i < players.Count; i++)
            {
                var player = (JsonObject)players[i];
                var seat = new JsonObject
                {
                    ["$type"] = "Seat",
                    ["id"] = Seat(i),
                    ["rank"] = Edits.Place(i),
                    ["color"] = player["color"]?.DeepClone(),
                    ["isAdmin"] = (bool?)player["isAdmin"] ?? false,
                };
                var name = (string)player["name"];
                if (name != $"Seat {i + 1}" && name != $"Player {i + 1}")
                    seat["name"] = name;
                writes.Insert(++at, seat);

                Hold(player["handRef"], new() { ["$type"] = "InHand", ["seat"] = Seat(i) });
                Hold(
                    player["cursorRef"],
                    new()
                    {
                        ["$type"] = "Held",
                        ["seat"] = Seat(i),
                        ["overHand"] = false,
                    }
                );
                Hold(
                    player["handHoldRef"],
                    new()
                    {
                        ["$type"] = "Held",
                        ["seat"] = Seat(i),
                        ["overHand"] = true,
                    }
                );
            }
        }

        foreach (var write in atoms)
        {
            switch ((string)write["$type"])
            {
                case "Prototype" when (string)write["parameters"]["$type"] == "ZoneParameters":
                    var parameters = (JsonObject)write["parameters"];
                    foreach (var key in new[] { "includedSeats", "excludedSeats" })
                    {
                        parameters[key] = new JsonArray([
                            .. ((JsonArray)parameters[key] ?? []).Select(n =>
                                (JsonNode)Seat((int)n)
                            ),
                        ]);
                    }
                    break;
                case "ComponentPosition" when write["containerRef"] is { } containerRef:
                    int index = write.IndexOf("containerRef");
                    write.RemoveAt(index);
                    var container =
                        holders.GetValueOrDefault((int)containerRef)?.DeepClone()
                        ?? new JsonObject
                        {
                            ["$type"] = "InComponent",
                            ["component"] = (int)containerRef,
                        };
                    write.Insert(index, "container", container);
                    break;
            }
        }

        void Hold(JsonNode tag, JsonObject container)
        {
            if (tag is JsonValue value && (int)value != 0)
                holders[(int)value] = container;
        }
    }
}

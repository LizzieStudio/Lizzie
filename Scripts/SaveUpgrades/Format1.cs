using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SaveUpgrades;

/// <summary>
/// Migrates the saves made before version numbering.
/// This one drops a handful of features.
/// </summary>
public sealed class Format1 : IStep
{
    public int Format => 1;

    public void Run(List<JsonObject> lines) => lines[0] = new Conversion().Run(lines.Single());

    private sealed class Conversion
    {
        private const int Stamp = 64;

        private readonly List<JsonNode> _writes = [];
        private readonly Dictionary<string, int> _tags = [];
        private readonly Dictionary<string, JsonObject> _images = [];
        private readonly Dictionary<string, Kind> _prototypes = [];
        private readonly Dictionary<string, List<string>> _rows = [];
        private readonly HashSet<string> _components = [];

        /// <summary>How a prototype's components become Components.</summary>
        /// <param name="Faces">
        /// A printed card's front and back keys, from its old number and its row's tag.
        /// </param>
        private sealed record Kind(
            int Type,
            int Mode,
            string Dataset,
            Func<int, int?, (JsonObject Front, JsonObject Back)> Faces
        );

        /// <summary>The SnowTag of something in the old save, made the first time it's asked for.</summary>
        private int Tag(string kind, string key) =>
            _tags.TryGetValue($"{kind}:{key}", out var tag)
                ? tag
                : _tags[$"{kind}:{key}"] = _tags.Count + 1;

        public JsonObject Run(JsonObject project)
        {
            Settings((JsonObject)project["GameSettings"]);

            foreach (var (key, image) in (JsonObject)project["Images"])
                _writes.Add(
                    _images[key] = new JsonObject
                    {
                        ["$type"] = "ImageAsset",
                        ["id"] = Tag("image", key),
                        ["name"] = (string)image["Name"],
                        ["cloudPath"] = (string)image["CloudPath"] ?? "",
                    }
                );

            foreach (var (name, dataset) in (JsonObject)project["Datasets"])
                Dataset(name, (JsonObject)dataset);

            foreach (var (name, template) in (JsonObject)project["Templates"])
            {
                var atom = new JsonObject
                {
                    ["$type"] = "Template",
                    ["id"] = Tag("template", name),
                    ["name"] = name,
                    ["elements"] = template["Elements"]?.DeepClone(),
                };
                Copy((JsonObject)template, atom, "SizeTemplate", "Width", "Height");
                if ((string)template["DataSet"] is { Length: > 0 } dataset)
                    atom["datasetRef"] = Tag("dataset", dataset);
                _writes.Add(atom);
            }

            foreach (var (key, prototype) in (JsonObject)project["Prototypes"])
                Prototype(key, (JsonObject)prototype);

            var snapshots = ((JsonObject)project["GameStates"])
                .Select(s => (JsonObject)s.Value)
                .OrderBy(s => (string)s["CapturedAt"], StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < snapshots.Count; i++)
                Blueprint(snapshots[i], i);
            if (snapshots.Count > 0)
                Table(Tag("snapshot", (string)snapshots[^1]["Name"]));

            return new JsonObject { ["id"] = Stamp * 2, ["writes"] = new JsonArray([.. _writes]) };
        }

        private void Settings(JsonObject old)
        {
            var settings = new JsonObject { ["$type"] = "ProjectGameSettings" };
            Copy(
                old,
                settings,
                "StartIn2D",
                "EnablePlayerHands",
                "TableWidth",
                "TableHeight",
                "TableUnits",
                "RotationStepIndex",
                "GameTitle",
                "Designers",
                "GraphicDesign",
                "Artists",
                "ContactInfo",
                "VisionStatement"
            );
            settings["tableColor"] = Color(old, "TableColor");
            _writes.Add(settings);

            var players = (JsonArray)old["Players"] ?? [];
            for (int i = 0; i < players.Count; i++)
            {
                var player = (JsonObject)players[i];
                var seat = new JsonObject
                {
                    ["$type"] = "Seat",
                    ["id"] = Tag("seat", i.ToString()),
                    ["rank"] = Rank(i),
                    ["color"] = Color(player, "Color"),
                    ["isAdmin"] = (bool?)player["IsAdmin"] ?? false,
                };
                // A seat with its default name goes by its number instead.
                var name = (string)player["Name"];
                if (name != $"Seat {i + 1}" && name != $"Player {i + 1}")
                    seat["name"] = name;
                _writes.Add(seat);
            }
        }

        /// <summary>
        /// The Seats of the old seat numbers in <paramref name="numbers"/>.
        /// </summary>
        private JsonArray Seats(JsonNode numbers) =>
            [.. ((JsonArray)numbers ?? []).Select(n => (JsonNode)Tag("seat", ((int)n).ToString()))];

        /// <summary>
        /// A dataset, with each row's name in a first column called Name.
        /// </summary>
        private void Dataset(string name, JsonObject old)
        {
            int dataset = Tag("dataset", name);
            _writes.Add(
                new JsonObject
                {
                    ["$type"] = "Dataset",
                    ["id"] = dataset,
                    ["name"] = name,
                }
            );

            var columns = new List<string> { "Name" };
            columns.AddRange(((JsonArray)old["Columns"]).Select(c => (string)c));
            var columnTags = columns
                .Select(
                    (column, i) =>
                    {
                        int tag = Tag("column", $"{name}/{i}");
                        _writes.Add(
                            new JsonObject
                            {
                                ["$type"] = "DataColumn",
                                ["id"] = tag,
                                ["datasetRef"] = dataset,
                                ["rank"] = Rank(i),
                                ["name"] = column,
                            }
                        );
                        return tag;
                    }
                )
                .ToList();

            var rows = _rows[name] = [];
            foreach (var (rowName, row) in (JsonObject)old["Rows"])
            {
                int tag = Tag("row", $"{name}/{rowName}");
                _writes.Add(
                    new JsonObject
                    {
                        ["$type"] = "DataRow",
                        ["id"] = tag,
                        ["datasetRef"] = dataset,
                        ["rank"] = Rank(rows.Count),
                    }
                );
                rows.Add(rowName);

                var values = new List<string> { rowName };
                values.AddRange(((JsonArray)row["Data"]).Select(v => (string)v));
                for (int i = 0; i < Math.Min(values.Count, columnTags.Count); i++)
                    if (!string.IsNullOrEmpty(values[i]))
                        _writes.Add(
                            new JsonObject
                            {
                                ["$type"] = "DataCell",
                                ["id"] = new JsonObject
                                {
                                    ["rowRef"] = tag,
                                    ["columnRef"] = columnTags[i],
                                },
                                ["value"] = values[i],
                            }
                        );
            }
        }

        private void Prototype(string key, JsonObject old)
        {
            var p = (JsonObject)old["Parameters"];
            int type = (int)old["Type"];
            int mode = (int?)p["Mode"] ?? 0;

            JsonObject parameters;
            Func<int, int?, (JsonObject, JsonObject)> faces = null;
            switch (type)
            {
                case 0:
                    parameters = Parameters(p, "CubeParameters", "Height", "Width", "Length");
                    parameters["color"] = Color(p, "Color");
                    break;
                case 1:
                    parameters = Parameters(p, "DiscParameters", "Height", "Diameter");
                    parameters["color"] = Color(p, "Color");
                    break;
                case 3 or 6 when Printed(key, p, type, mode, out faces) is { } printed:
                    parameters = printed;
                    break;
                case 7:
                    parameters = Parameters(p, "DieParameters", "Size", "SideCount");
                    parameters["color"] = Color(p, "Color");
                    parameters["faces"] =
                        mode == 3
                            ? TemplateSide(p, "FrontTemplate")
                            : QuickSide(
                                ((JsonArray)p["Sides"] ?? []).Select(
                                    (side, i) =>
                                        Suit(
                                            p,
                                            "Color",
                                            (JsonObject)side,
                                            Tag("suit", $"{key}/{i}")
                                        )
                                )
                            );
                    break;
                case 9:
                    parameters = Parameters(p, "MeepleParameters", "Height", "Thickness");
                    parameters["color"] = Color(p, "Color");
                    parameters["grid"] = p["Grid"]?.DeepClone();
                    break;
                case 10:
                    parameters = Parameters(p, "TrayParameters", "Height", "Width", "Length");
                    parameters["color"] = Color(p, "Color");
                    if ((string)p["Prototype"] is { Length: > 0 } contents)
                        parameters["dispenseRef"] = Tag("prototype", contents);
                    break;
                case 11:
                    parameters = Parameters(p, "BagParameters", "Height", "Diameter", "ShowCount");
                    parameters["color"] = Color(p, "Color");
                    break;
                case 12:
                    parameters = Parameters(
                        p,
                        "ZoneParameters",
                        "Width",
                        "Depth",
                        "DefaultIncluded",
                        "HiddenWhenExcluded"
                    );
                    parameters["includedSeats"] = Seats(p["IncludedSeats"]);
                    parameters["excludedSeats"] = Seats(p["ExcludedSeats"]);
                    break;
                default:
                    return;
            }

            _prototypes[key] = new Kind(type, mode, (string)p["Dataset"], faces);
            _writes.Add(
                new JsonObject
                {
                    ["$type"] = "Prototype",
                    ["id"] = Tag("prototype", key),
                    ["name"] = (string)old["Name"],
                    ["parameters"] = parameters,
                }
            );
        }

        /// <summary>
        /// A token's or deck's parameters, or null for a kind there's no longer any way to make.
        /// </summary>
        /// <param name="faces">Gets a card's face keys, as <see cref="Kind.Faces"/>.</param>
        private JsonObject Printed(
            string key,
            JsonObject p,
            int type,
            int mode,
            out Func<int, int?, (JsonObject, JsonObject)> faces
        )
        {
            bool differentBack = (bool?)p["DifferentBack"] ?? false;
            JsonObject front,
                back = null,
                backFace = null;
            switch (mode)
            {
                case 0:
                    var quickFront = (JsonObject)p["QuickFront"];
                    int frontSuit = Tag("suit", $"{key}/front");
                    front = QuickSide([Suit(p, "FrontBgColor", quickFront, frontSuit)]);
                    var frontFace = QuickFace(frontSuit, (string)quickFront?["Caption"]);
                    if (differentBack && p["QuickBack"] is JsonObject quickBack)
                    {
                        int backSuit = Tag("suit", $"{key}/back");
                        back = QuickSide([
                            Suit(p, "BackBgColor", quickBack, backSuit, quantity: false),
                        ]);
                        backFace = QuickFace(backSuit, (string)quickBack["Caption"]);
                    }
                    faces = (_, _) => (frontFace, backFace);
                    break;
                case 5:
                    var cards = ((JsonArray)p["QuickCardData"] ?? []).Cast<JsonObject>().ToList();
                    var suits = cards.Select((_, i) => Tag("suit", $"{key}/front/{i}")).ToList();
                    front = QuickSide(
                        cards.Select(
                            (card, i) =>
                                new JsonObject
                                {
                                    ["id"] = suits[i],
                                    ["background"] = Color(card, "BackgroundColor"),
                                    ["values"] = Quote((string)card["Caption"]),
                                }
                        )
                    );
                    var fronts = cards
                        .Select((card, i) => QuickFace(suits[i], (string)card["Caption"]))
                        .ToList();
                    if (differentBack && cards.Count > 0)
                    {
                        int backSuit = Tag("suit", $"{key}/back");
                        var backValue = (string)cards[0]["CardBackValue"];
                        back = QuickSide([
                            new JsonObject
                            {
                                ["id"] = backSuit,
                                ["background"] = Color(cards[0], "CardBackColor"),
                                ["values"] = Quote(backValue),
                            },
                        ]);
                        backFace = QuickFace(backSuit, backValue);
                    }
                    faces = (n, _) => (fronts.ElementAtOrDefault(n), backFace);
                    break;
                case 2:
                    front = ImageSide((string)p["FrontGridImageKey"], "", p);
                    bool singleBack = (bool?)p["GridSingleBack"] ?? false;
                    bool imageBack =
                        differentBack && (string)p["BackGridImageKey"] is { Length: > 0 };
                    if (imageBack)
                        back = ImageSide((string)p["BackGridImageKey"], singleBack ? "1" : "", p);
                    faces = (n, _) =>
                        (FrameFace(n + 1), imageBack ? FrameFace(singleBack ? 1 : n + 1) : null);
                    break;
                case 3:
                    front = TemplateSide(p, "FrontTemplate");
                    bool templateBack = (string)p["BackTemplate"] is { Length: > 0 };
                    if (templateBack)
                        back = TemplateSide(p, "BackTemplate");
                    faces = (_, row) =>
                        row is int r
                            ? (RowFace(r), templateBack ? RowFace(r) : null)
                            : (null, null);
                    break;
                default:
                    faces = null;
                    return null;
            }

            var parameters = Parameters(
                p,
                "PrintedParameters",
                "Height",
                "Width",
                "Thickness",
                "Shape",
                "Type"
            );
            parameters["front"] = front;
            if (back != null)
                parameters["back"] = back;
            if (type == 6)
                parameters["spawnDeck"] = true;
            return parameters;
        }

        private static JsonObject QuickSide(IEnumerable<JsonObject> suits) =>
            new() { ["$type"] = "QuickSide", ["suits"] = new JsonArray([.. suits]) };

        /// <summary>
        /// The key of the card showing the <see cref="Quote"/>d <paramref name="text"/> in <paramref name="suit"/>.
        /// </summary>
        private static JsonObject QuickFace(int suit, string text) =>
            new()
            {
                ["$type"] = "QuickFace",
                ["suitRef"] = suit,
                ["value"] = Literal(text),
            };

        private static JsonObject FrameFace(int frame) =>
            new() { ["$type"] = "FrameFace", ["frame"] = frame };

        private static JsonObject RowFace(int row) =>
            new() { ["$type"] = "RowFace", ["rowRef"] = row };

        /// <summary>
        /// A quick suit from an old face: its text, repeated by its quantity, in its color,
        /// on the background named by <paramref name="background"/>.
        /// </summary>
        private static JsonObject Suit(
            JsonObject p,
            string background,
            JsonObject face,
            int id,
            bool quantity = true
        )
        {
            int count = quantity ? (int?)face["Quantity"] ?? 1 : 1;
            var values = Quote((string)face["Caption"]);
            return new JsonObject
            {
                ["id"] = id,
                ["background"] = Color(p, background),
                ["foreground"] = Color(face, "ForegroundColor"),
                ["values"] = count > 1 ? $"{values}*{count}" : values,
            };
        }

        /// <summary>
        /// An image side, giving the image the prototype's grid.
        /// </summary>
        private JsonObject ImageSide(string image, string frames, JsonObject p)
        {
            if (_images.TryGetValue(image, out var asset) && asset["layout"] == null)
                asset["layout"] = new JsonObject
                {
                    ["columns"] = (int?)p["GridCols"] ?? 1,
                    ["rows"] = (int?)p["GridRows"] ?? 1,
                    ["count"] = (int?)p["GridCount"],
                };
            return new JsonObject
            {
                ["$type"] = "ImageSide",
                ["imageRef"] = Tag("image", image),
                ["frames"] = frames,
            };
        }

        private JsonObject TemplateSide(JsonObject p, string template)
        {
            var side = new JsonObject
            {
                ["$type"] = "TemplateSide",
                ["templateRef"] = Tag("template", (string)p[template] ?? ""),
            };
            if ((string)p["Dataset"] is { Length: > 0 } dataset)
                side["datasetRef"] = Tag("dataset", dataset);
            return side;
        }

        /// <summary>
        /// The current table, started empty on <paramref name="blueprint"/>.
        /// </summary>
        private void Table(int blueprint)
        {
            int table = Tag("table", "");
            _writes.Add(
                new JsonObject
                {
                    ["$type"] = "Stage",
                    ["id"] = table,
                    ["name"] = "Table 1",
                    ["parentRef"] = blueprint,
                    ["rank"] = Rank(0),
                    ["isTable"] = true,
                }
            );
            _writes.Add(new JsonObject { ["$type"] = "CurrentTable", ["stageRef"] = table });
        }

        /// <summary>
        /// A snapshot as a root blueprint, holding a position for each component.
        /// </summary>
        private void Blueprint(JsonObject snapshot, int index)
        {
            int stage = Tag("snapshot", (string)snapshot["Name"]);
            _writes.Add(
                new JsonObject
                {
                    ["$type"] = "Stage",
                    ["id"] = stage,
                    ["name"] = (string)snapshot["Name"],
                    ["rank"] = Rank(index),
                }
            );

            var components = ((JsonArray)snapshot["Components"]).Cast<JsonObject>().ToList();
            var containers = new Dictionary<string, string>();
            foreach (var c in components)
            foreach (var contained in (JsonArray)c["ContainedComponents"] ?? [])
                containers[(string)contained] = (string)c["ComponentRef"];

            var kept = components
                .Where(c => _prototypes.ContainsKey((string)c["PrototypeRef"]))
                .Select(c => (string)c["ComponentRef"])
                .ToHashSet();
            var prototypes = components.ToDictionary(
                c => (string)c["ComponentRef"],
                c => (string)c["PrototypeRef"]
            );

            // Ranked by their order in the stack, so no two share a rank.
            foreach (var (order, c) in components.OrderBy(c => (int?)c["ZOrder"] ?? 0).Index())
            {
                string id = (string)c["ComponentRef"];
                if (!kept.Contains(id))
                    continue;
                var container = containers.GetValueOrDefault(id);
                if (container != null && !kept.Contains(container))
                    continue;

                Component(
                    id,
                    (string)c["PrototypeRef"],
                    (string)c["DatasetRow"] ?? "",
                    container != null && _prototypes[prototypes[container]].Type == 6
                        ? Tag("component", container)
                        : null
                );
                var position = new JsonObject
                {
                    ["$type"] = "ComponentPosition",
                    ["id"] = Tag("component", id),
                    ["stageRef"] = stage,
                    ["rank"] = Rank(order),
                };
                if (container != null)
                    position["container"] = new JsonObject
                    {
                        ["$type"] = "InComponent",
                        ["component"] = Tag("component", container),
                    };
                else
                {
                    position["x"] = (int)Math.Round(((double?)c["Px"] ?? 0) * 100);
                    position["z"] = (int)Math.Round(((double?)c["Pz"] ?? 0) * 100);
                }
                double[] rotation =
                [
                    (double?)c["Rx"] ?? 0,
                    (double?)c["Ry"] ?? 0,
                    (double?)c["Rz"] ?? 0,
                ];
                if (rotation.Any(r => r != 0))
                    position["rotation"] = new JsonArray([.. rotation.Select(r => (JsonNode)r)]);
                _writes.Add(position);
            }
        }

        /// <summary>
        /// Writes what <paramref name="component"/> is, the first time it appears,
        /// since a component in several snapshots is one component.
        /// </summary>
        /// <param name="issuer">The tag of the deck holding the component, if any, which issued it.</param>
        private void Component(string component, string prototype, string row, int? issuer)
        {
            if (!_components.Add(component))
                return;

            var written = new JsonObject
            {
                ["$type"] = "Component",
                ["id"] = Tag("component", component),
                ["prototypeRef"] = Tag("prototype", prototype),
            };
            if (issuer != null)
                written["deckRef"] = issuer;
            var kind = _prototypes[prototype];
            var rows = kind.Dataset is { } dataset ? _rows.GetValueOrDefault(dataset) ?? [] : [];
            int index = rows.IndexOf(row);
            int? rowTag = index >= 0 ? Tag("row", $"{kind.Dataset}/{row}") : null;

            // a deck holds its cards rather than being one
            bool deck = kind.Type == 6 && row == "";
            if (kind.Type is 3 or 6 && !deck)
            {
                int.TryParse(row, out int number);
                var (front, back) = kind.Faces(
                    kind.Mode == 5 ? Math.Max(number - 1, 0) : number,
                    rowTag
                );
                var item = new JsonObject { ["$type"] = "PrintedItem" };
                if (front != null)
                    item["front"] = front.DeepClone();
                if (back != null)
                    item["back"] = back.DeepClone();
                written["item"] = item;
            }
            else if (kind.Type == 7 && rowTag != null)
                written["item"] = new JsonObject { ["$type"] = "DieItem", ["rowRef"] = rowTag };

            _writes.Add(written);
        }

        /// <summary>
        /// Parameters of <paramref name="type"/> with the listed properties copied.
        /// </summary>
        private static JsonObject Parameters(JsonObject p, string type, params string[] names)
        {
            var parameters = new JsonObject { ["$type"] = type };
            Copy(p, parameters, names);
            return parameters;
        }

        /// <summary>
        /// Copies the old PascalCase properties that are there to their camelCase names.
        /// </summary>
        private static void Copy(JsonObject from, JsonObject to, params string[] names)
        {
            foreach (var name in names)
                if (from[name] is { } value)
                    to[char.ToLowerInvariant(name[0]) + name[1..]] = value.DeepClone();
        }

        /// <summary>
        /// A color, from an old Color object, or from the four floats named like <c>TableColorR</c>.
        /// </summary>
        private static JsonObject Color(JsonObject from, string name)
        {
            string[] channels = ["R", "G", "B", "A"];
            double[] rgba = from[name] is JsonObject color
                ? channels.Select(c => ((double?)color[c + "8"] ?? 255) / 255).ToArray()
                : channels.Select(c => (double?)from[name + c] ?? 1).ToArray();
            return new JsonObject
            {
                ["args"] = new JsonArray([.. rgba.Select(c => (JsonNode)c)]),
                ["type"] = "Color",
            };
        }

        /// <summary>A sequence expression that's exactly <paramref name="text"/>.</summary>
        private static string Quote(string text) => $"\"{Literal(text)}\"";

        /// <summary>
        /// The value a <see cref="Quote"/>d <paramref name="text"/> makes.
        /// </summary>
        private static string Literal(string text) => (text ?? "").Replace("\"", "'");

        /// <summary>
        /// The rank of the item at <paramref name="index"/>, an end rank with the shared stamp,
        /// placed by three digits of the ordered characters <c>-</c> through <c>z</c>.
        /// </summary>
        private static JsonObject Rank(int index)
        {
            const string digits =
                "-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            return new JsonObject
            {
                ["side"] = 1,
                ["steps"] = new JsonArray(
                    new JsonObject
                    {
                        ["stamp"] = Stamp,
                        ["place"] =
                            $"{digits[index / 4096 % 64]}{digits[index / 64 % 64]}{digits[index % 64]}",
                    }
                ),
            };
        }
    }
}

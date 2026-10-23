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

        /// <summary>How a prototype's components become variants.</summary>
        private sealed record Kind(int Type, int Mode, string Dataset);

        /// <summary>The SnowTag of something in the old save, made the first time it's asked for.</summary>
        private int Tag(string kind, string key) =>
            _tags.TryGetValue($"{kind}:{key}", out var tag)
                ? tag
                : _tags[$"{kind}:{key}"] = _tags.Count + 1;

        public JsonObject Run(JsonObject project)
        {
            var settings = Settings((JsonObject)project["GameSettings"]);

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
                DataSet(name, (JsonObject)dataset);

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
                    atom["dataSet"] = Tag("dataset", dataset);
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
                Game(Tag("snapshot", (string)snapshots[^1]["Name"]));

            return new JsonObject { ["id"] = Stamp * 2, ["writes"] = new JsonArray([.. _writes]) };
        }

        private JsonObject Settings(JsonObject old)
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
                "AllowObservers",
                "MaxPlayers",
                "GameTitle",
                "Designers",
                "GraphicDesign",
                "Artists",
                "ContactInfo",
                "VisionStatement"
            );
            settings["tableColor"] = Color(old, "TableColor");
            if (old["Players"] is JsonArray players)
                settings["players"] = new JsonArray([
                    .. players.Select(p => new JsonObject
                    {
                        ["name"] = (string)p["Name"],
                        ["color"] = Color((JsonObject)p, "Color"),
                        ["isAdmin"] = (bool?)p["IsAdmin"] ?? false,
                    }),
                ]);
            _writes.Add(settings);
            return settings;
        }

        /// <summary>
        /// A dataset, with each row's name in a first column called Name.
        /// </summary>
        private void DataSet(string name, JsonObject old)
        {
            int dataset = Tag("dataset", name);
            _writes.Add(
                new JsonObject
                {
                    ["$type"] = "DataSet",
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
                                ["dataSetId"] = dataset,
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
                        ["dataSetId"] = dataset,
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
                                    ["row"] = tag,
                                    ["column"] = columnTags[i],
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
                case 3 or 6 when Printed(p, type, mode) is { } printed:
                    parameters = printed;
                    break;
                case 7:
                    parameters = Parameters(p, "DieParameters", "Size", "SideCount");
                    parameters["color"] = Color(p, "Color");
                    parameters["faces"] =
                        mode == 3
                            ? TemplateSide(p, "FrontTemplate")
                            : QuickSide(
                                ((JsonArray)p["Sides"] ?? []).Select(side =>
                                    Suit(p, "Color", (JsonObject)side)
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
                        parameters["prototype"] = Tag("prototype", contents);
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
                        "HiddenWhenExcluded",
                        "IncludedSeats",
                        "ExcludedSeats"
                    );
                    break;
                default:
                    return;
            }

            _prototypes[key] = new Kind(type, mode, (string)p["Dataset"]);
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
        private JsonObject Printed(JsonObject p, int type, int mode)
        {
            bool differentBack = (bool?)p["DifferentBack"] ?? false;
            JsonObject front,
                back = null;
            switch (mode)
            {
                case 0:
                    front = QuickSide([Suit(p, "FrontBgColor", (JsonObject)p["QuickFront"])]);
                    if (differentBack && p["QuickBack"] is JsonObject quickBack)
                        back = QuickSide([Suit(p, "BackBgColor", quickBack, quantity: false)]);
                    break;
                case 5:
                    var cards = ((JsonArray)p["QuickCardData"] ?? []).Cast<JsonObject>().ToList();
                    front = QuickSide(
                        cards.Select(card => new JsonObject
                        {
                            ["background"] = Color(card, "BackgroundColor"),
                            ["values"] = (string)card["Caption"] ?? "",
                        })
                    );
                    if (differentBack && cards.Count > 0)
                        back = QuickSide([
                            new JsonObject
                            {
                                ["background"] = Color(cards[0], "CardBackColor"),
                                ["values"] = Quote((string)cards[0]["CardBackValue"]),
                            },
                        ]);
                    break;
                case 2:
                    front = ImageSide((string)p["FrontGridImageKey"], "", p);
                    if (differentBack && (string)p["BackGridImageKey"] is { Length: > 0 } backImage)
                        back = ImageSide(
                            backImage,
                            (bool?)p["GridSingleBack"] ?? false ? "1" : "",
                            p
                        );
                    break;
                case 3:
                    front = TemplateSide(p, "FrontTemplate");
                    if ((string)p["BackTemplate"] is { Length: > 0 })
                        back = TemplateSide(p, "BackTemplate");
                    break;
                default:
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
        /// A quick suit from an old face: its text, repeated by its quantity, in its color,
        /// on the background named by <paramref name="background"/>.
        /// </summary>
        private static JsonObject Suit(
            JsonObject p,
            string background,
            JsonObject face,
            bool quantity = true
        )
        {
            int count = quantity ? (int?)face["Quantity"] ?? 1 : 1;
            var values = Quote((string)face["Caption"]);
            return new JsonObject
            {
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
                ["image"] = Tag("image", image),
                ["frames"] = frames,
            };
        }

        private JsonObject TemplateSide(JsonObject p, string template)
        {
            var side = new JsonObject
            {
                ["$type"] = "TemplateSide",
                ["template"] = Tag("template", (string)p[template] ?? ""),
            };
            if ((string)p["Dataset"] is { Length: > 0 } dataset)
                side["dataset"] = Tag("dataset", dataset);
            return side;
        }

        /// <summary>
        /// The current game, started empty on <paramref name="blueprint"/>.
        /// </summary>
        private void Game(int blueprint)
        {
            int game = Tag("game", "");
            _writes.Add(
                new JsonObject
                {
                    ["$type"] = "Stage",
                    ["id"] = game,
                    ["name"] = "Game 1",
                    ["parentRef"] = blueprint,
                    ["rank"] = Rank(0),
                    ["isGame"] = true,
                }
            );
            _writes.Add(new JsonObject { ["$type"] = "CurrentGame", ["stageRef"] = game });
        }

        /// <summary>
        /// A snapshot as a root blueprint, holding a position and a variant for each component.
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

            foreach (var c in components)
            {
                string id = (string)c["ComponentRef"];
                if (!kept.Contains(id))
                    continue;
                var container = containers.GetValueOrDefault(id);
                if (container != null && !kept.Contains(container))
                    continue;

                var position = new JsonObject
                {
                    ["$type"] = "ComponentPosition",
                    ["id"] = Tag("component", id),
                    ["stage"] = stage,
                    ["variantRef"] = Variant(
                        (string)c["PrototypeRef"],
                        (string)c["DataSetRow"] ?? ""
                    ),
                    ["zOrder"] = new JsonObject
                    {
                        ["target"] = 1,
                        ["suborder"] = Math.Max((int?)c["ZOrder"] ?? 0, 0),
                        ["stamp"] = Stamp,
                    },
                };
                if (container != null)
                    position["containerRef"] = Tag("component", container);
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
        /// The variant of a prototype's item, shared by every component of the same item.
        /// </summary>
        private int Variant(string prototype, string row)
        {
            string key = $"{prototype}/{row}";
            if (_tags.ContainsKey($"variant:{key}"))
                return Tag("variant", key);

            int tag = Tag("variant", key);
            var variant = new JsonObject
            {
                ["$type"] = "ComponentVariant",
                ["id"] = tag,
                ["prototypeRef"] = Tag("prototype", prototype),
            };
            var kind = _prototypes[prototype];
            var rows = kind.Dataset is { } dataset ? _rows.GetValueOrDefault(dataset) ?? [] : [];
            int index = rows.IndexOf(row);
            int? rowTag = index >= 0 ? Tag("row", $"{kind.Dataset}/{row}") : null;

            // a deck holds its cards rather than being one
            bool deck = kind.Type == 6 && row == "";
            if (kind.Type is 3 or 6 && !deck)
            {
                int.TryParse(row, out int number);
                variant["item"] = new JsonObject
                {
                    ["$type"] = "PrintedItem",
                    ["number"] = kind.Mode switch
                    {
                        5 => Math.Max(number - 1, 0),
                        3 => Math.Max(index, 0),
                        _ => number,
                    },
                    ["frontRow"] = rowTag ?? 0,
                    ["backRow"] = rowTag ?? 0,
                };
            }
            else if (kind.Type == 7 && rowTag != null)
                variant["item"] = new JsonObject { ["$type"] = "DieItem", ["row"] = rowTag };

            _writes.Add(variant);
            return tag;
        }

        /// <summary>
        /// Parameters of <paramref name="type"/> with their name and the listed properties copied.
        /// </summary>
        private static JsonObject Parameters(JsonObject p, string type, params string[] names)
        {
            var parameters = new JsonObject { ["$type"] = type };
            Copy(p, parameters, ["ComponentName", "BaseName", .. names]);
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
        private static string Quote(string text) => $"\"{(text ?? "").Replace("\"", "'")}\"";

        /// <summary>
        /// The rank of the item at <paramref name="index"/>, three digits of the ordered characters <c>-</c> through <c>z</c>.
        /// </summary>
        private static string Rank(int index)
        {
            const string digits =
                "-/0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            return $"{digits[index / 4096 % 64]}{digits[index / 64 % 64]}{digits[index % 64]}";
        }
    }
}

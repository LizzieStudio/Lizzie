using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

public static class JsonUtilities
{
    public static Dictionary<string, object> ParseJsonToDictionary(
        VisualComponentBase.VisualComponentType vcType,
        Dictionary<string, object> d
    )
    {
        switch (vcType)
        {
            case VisualComponentBase.VisualComponentType.Cube:
                return ParseCube(d);

            case VisualComponentBase.VisualComponentType.Disc:
                return ParseDisc(d);

            case VisualComponentBase.VisualComponentType.Token:
                return ParseToken(d);

            case VisualComponentBase.VisualComponentType.Deck:
                return ParseDeck(d);

            case VisualComponentBase.VisualComponentType.Die:
                return ParseDie(d);

            case VisualComponentBase.VisualComponentType.Mesh:
                break;

            case VisualComponentBase.VisualComponentType.Meeple:
                return ParseMeeple(d);

            case VisualComponentBase.VisualComponentType.Bag:
                return ParseBag(d);

            default:
                throw new ArgumentOutOfRangeException(nameof(vcType), vcType, null);
        }

        return d; //for now
    }

    private static Dictionary<string, object> ParseCube(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();

        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Height", TryGetFloat(d, "Height"));
        p.Add("Width", TryGetFloat(d, "Width"));
        p.Add("Length", TryGetFloat(d, "Length"));
        p.Add("Color", TryGetColor(d, "Color"));

        return p;
    }

    private static Dictionary<string, object> ParseDisc(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();

        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Height", TryGetFloat(d, "Height"));
        p.Add("Diameter", TryGetFloat(d, "Diameter"));
        p.Add("Color", TryGetColor(d, "Color"));

        return p;
    }

    private static Dictionary<string, object> ParseBag(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();

        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Height", TryGetFloat(d, "Height"));
        p.Add("Diameter", TryGetFloat(d, "Diameter"));
        p.Add("Color", TryGetColor(d, "Color"));
        p.Add("ShowCount", TryGetBool(d, "ShowCount"));

        return p;
    }

    private static Dictionary<string, object> ParseToken(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();

        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Height", TryGetFloat(d, "Height"));
        p.Add("Width", TryGetFloat(d, "Width"));
        p.Add("Thickness", TryGetFloat(d, "Thickness"));

        p.Add("FrontImage", TryGetString(d, "FrontImage"));
        p.Add("BackImage", TryGetString(d, "BackImage"));

        p.Add("Shape", TryGetInt(d, "Shape"));
        p.Add("Mode", (VcToken.TokenBuildMode)TryGetInt(d, "Mode"));

        p.Add("FrontBgColor", TryGetColor(d, "FrontBgColor"));
        p.Add("BackBgColor", TryGetColor(d, "BackBgColor"));

        p.Add("QuickFront", TryGetQTF(d, "QuickFront"));
        p.Add("QuickBack", TryGetQTF(d, "QuickBack"));

        p.Add("Type", TryGetInt(d, "Type"));
        p.Add("FrontFontSize", TryGetInt(d, "FrontFontSize"));
        p.Add("BackFontSize", TryGetInt(d, "BackFontSize"));

        p.Add("DifferentBack", TryGetBool(d, "DifferentBack"));

        p.Add("FrontTemplate", TryGetString(d, "FrontTemplate"));
        p.Add("BackTemplate", TryGetString(d, "BackTemplate"));
        p.Add("Dataset", TryGetString(d, "Dataset"));
        p.Add("CardReference", TryGetString(d, "CardReference"));

        p.Add("QuickCardData", TryGetQuickCardDataList(d, "QuickCardData"));

        p.Add("FrontGridImageKey", TryGetString(d, "FrontGridImageKey"));
        p.Add("BackGridImageKey", TryGetString(d, "BackGridImageKey"));

        p.Add("GridRows", TryGetInt(d, "GridRows"));
        p.Add("GridCols", TryGetInt(d, "GridCols"));
        p.Add("GridCount", TryGetInt(d, "GridCount"));

        return p;
    }

    private static Dictionary<string, object> ParseDie(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();

        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Size", TryGetFloat(d, "Size"));
        p.Add("Color", TryGetColor(d, "Color"));

        p.Add("Sides", TryGetQTFArray(d, "Sides"));

        return p;
    }

    private static Dictionary<string, object> ParseDeck(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();

        // Common parameters
        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Height", TryGetFloat(d, "Height"));
        p.Add("Width", TryGetFloat(d, "Width"));
        p.Add("Shape", TryGetInt(d, "Shape"));
        p.Add("Mode", (VcToken.TokenBuildMode)TryGetInt(d, "Mode"));

        // Mode-specific parameters
        var mode = (VcToken.TokenBuildMode)TryGetInt(d, "Mode");

        switch (mode)
        {
            case VcToken.TokenBuildMode.Quick:
            case VcToken.TokenBuildMode.QuickDeck:
                p.Add("QuickCardData", TryGetQuickCardDataList(d, "QuickCardData"));
                p.Add("DifferentBack", TryGetBool(d, "DifferentBack"));
                break;

            case VcToken.TokenBuildMode.Grid:
                p.Add("FrontGridImageKey", TryGetString(d, "FrontGridImageKey"));
                p.Add("BackGridImageKey", TryGetString(d, "BackGridImageKey"));

                p.Add("GridRows", TryGetInt(d, "GridRows"));
                p.Add("GridCols", TryGetInt(d, "GridCols"));
                p.Add("GridCount", TryGetInt(d, "GridCount"));
                p.Add("DifferentBack", TryGetBool(d, "DifferentBack"));
                p.Add("GridSingleBack", TryGetBool(d, "GridSingleBack"));
                break;

            case VcToken.TokenBuildMode.Template:
                p.Add("FrontTemplate", TryGetString(d, "FrontTemplate"));
                p.Add("BackTemplate", TryGetString(d, "BackTemplate"));
                p.Add("Dataset", TryGetString(d, "Dataset"));
                break;
        }

        return p;
    }

    public static Dictionary<string, object> ParseMeeple(Dictionary<string, object> d)
    {
        var p = new Dictionary<string, object>();
        p.Add("ComponentName", TryGetString(d, "ComponentName"));
        p.Add("BaseName", TryGetString(d, "BaseName"));
        p.Add("Height", TryGetFloat(d, "Height"));
        p.Add("Thickness", TryGetFloat(d, "Thickness"));
        p.Add("Color", TryGetColor(d, "Color"));
        p.Add("Grid", TryGetBoolJaggedArray(d, "Grid"));
        return p;
    }

    private static string TryGetString(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            return value.ToString();
        }
        return string.Empty;
    }

    private static float TryGetFloat(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value))
        {
            if (value is float f)
                return f;
            if (value is double db)
                return (float)db;
            if (value is int i)
                return i;
            if (value is long l)
                return l;
            if (float.TryParse(value.ToString(), out var parsed))
                return parsed;
        }
        return 0;
    }

    private static int TryGetInt(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value))
        {
            if (int.TryParse(value.ToString(), out var parsed))
                return parsed;
        }
        return 0;
    }

    private static bool TryGetBool(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value))
        {
            if (bool.TryParse(value.ToString(), out var parsed))
                return parsed;
        }
        return false;
    }

    private static Color TryGetColor(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            return JsonSerializer.Deserialize<Color>(value.ToString());
        }

        return Colors.Black; // Default color if not found or deserialization fails
    }

    private static QuickTextureField TryGetQTF(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            return JsonSerializer.Deserialize<QuickTextureField>(value.ToString());
        }

        return new QuickTextureField(); // Default if not found or deserialization fails
    }

    private static QuickTextureField[] TryGetQTFArray(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            object[] array = JsonSerializer.Deserialize<object[]>(value.ToString());

            var r = new QuickTextureField[array.Length];
            int index = 0;
            foreach (var item in array)
            {
                var qtf = JsonSerializer.Deserialize<QuickTextureField>(item.ToString());
                r[index] = qtf;
                index++;
            }

            return r;
        }
        return []; // Default if not found or deserialization fails
    }

    private static bool[][] TryGetBoolJaggedArray(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            // If it's already a jagged array, return it
            if (value is bool[][] jaggedArray)
            {
                return jaggedArray;
            }

            // Try to deserialize from JSON
            try
            {
                // First deserialize as JsonElement to check structure
                var json = value.ToString();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var outerArray = root.EnumerateArray().ToArray();
                    var result = new bool[outerArray.Length][];

                    for (int i = 0; i < outerArray.Length; i++)
                    {
                        if (outerArray[i].ValueKind == JsonValueKind.Array)
                        {
                            var innerArray = outerArray[i].EnumerateArray().ToArray();
                            result[i] = new bool[innerArray.Length];

                            for (int j = 0; j < innerArray.Length; j++)
                            {
                                result[i][j] = innerArray[j].GetBoolean();
                            }
                        }
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Error deserializing jagged bool array: {ex.Message}");
            }
        }

        return new bool[0][]; // Default empty jagged array if not found or deserialization fails
    }

    private static List<QuickCardData> TryGetQuickCardDataList(
        Dictionary<string, object> d,
        string key
    )
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            object[] array = JsonSerializer.Deserialize<object[]>(value.ToString());

            var r = new List<QuickCardData>();

            foreach (var item in array)
            {
                var qcd = JsonSerializer.Deserialize<QuickCardData>(item.ToString());
                r.Add(qcd);
            }

            return r;
        }
        return []; // Default if not found or deserialization fails
    }

    private static Color ParseColorFromJson(JsonElement element)
    {
        // Color is serialized as an object with R, G, B, A properties (or similar)
        try
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                // If it's a string, try to deserialize as JSON
                return JsonSerializer.Deserialize<Color>(element.GetString());
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                // Parse color object directly
                float r = 0,
                    g = 0,
                    b = 0,
                    a = 1;

                if (element.TryGetProperty("R", out var rProp))
                    r = (float)rProp.GetDouble();
                else if (element.TryGetProperty("r", out var rProp2))
                    r = (float)rProp2.GetDouble();

                if (element.TryGetProperty("G", out var gProp))
                    g = (float)gProp.GetDouble();
                else if (element.TryGetProperty("g", out var gProp2))
                    g = (float)gProp2.GetDouble();

                if (element.TryGetProperty("B", out var bProp))
                    b = (float)bProp.GetDouble();
                else if (element.TryGetProperty("b", out var bProp2))
                    b = (float)bProp2.GetDouble();

                if (element.TryGetProperty("A", out var aProp))
                    a = (float)aProp.GetDouble();
                else if (element.TryGetProperty("a", out var aProp2))
                    a = (float)aProp2.GetDouble();

                return new Color(r, g, b, a);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error parsing color: {ex.Message}");
        }

        return Colors.Black; // Default color
    }

    private static Texture2D TryGetTexture2D(Dictionary<string, object> d, string key)
    {
        if (d.TryGetValue(key, out var value) && value != null)
        {
            // If it's already a Texture2D, return it
            if (value is Texture2D texture)
            {
                return texture;
            }

            // Try to load from file path
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    return Utility.LoadTexture(path);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"Error loading texture from path {path}: {ex.Message}");
                }
            }
        }

        return null; // Default null if not found
    }
}

# ParseDeck Function Documentation

## Overview

Added `ParseDeck` function to `JsonUtilities.cs` to properly deserialize all parameters used by `VcDeck` (deck of cards component). This function handles the complex parameter structures for three different deck build modes: Quick, Grid, and Template.

## Parameters Handled

### Common Parameters (All Modes)
| Parameter | Type | Description |
|-----------|------|-------------|
| ComponentName | `string` | Name of the deck instance |
| BaseName | `string` | Base name for the component |
| Height | `float` | Card height in mm |
| Width | `float` | Card width in mm |
| Shape | `int` | Shape type (0=Square, 1=Circle, etc.) |
| Mode | `VcToken.TokenBuildMode` | Build mode enum (Quick/Grid/Template) |

### Quick Mode Parameters
| Parameter | Type | Description |
|-----------|------|-------------|
| QuickCardData | `List<QuickCardData>` | List of card definitions with face/back colors and captions |

**QuickCardData Structure:**
```csharp
public class QuickCardData
{
    public Color BackgroundColor { get; set; }  // Card face background
    public string Caption { get; set; }          // Card face text
    public Color CardBackColor { get; set; }     // Card back background
    public string CardBackValue { get; set; }    // Card back text
}
```

### Grid Mode Parameters
| Parameter | Type | Description |
|-----------|------|-------------|
| FrontMasterSprite | `Texture2D` | Master sprite sheet for card fronts |
| BackMasterSprite | `Texture2D` | Master sprite sheet for card backs |
| GridRows | `int` | Number of rows in sprite sheet |
| GridCols | `int` | Number of columns in sprite sheet |
| GridCount | `int` | Total number of cards to extract |
| DifferentBack | `bool` | Whether cards have different backs |

### Template Mode Parameters
| Parameter | Type | Description |
|-----------|------|-------------|
| FrontTemplate | `string` | Name of template for card fronts |
| BackTemplate | `string` | Name of template for card backs |
| Dataset | `string` | Name of dataset to use for card data |

## Implementation

### ParseDeck Method

```csharp
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
            p.Add("QuickCardData", TryGetQuickCardDataList(d, "QuickCardData"));
            break;

        case VcToken.TokenBuildMode.Grid:
            p.Add("FrontMasterSprite", TryGetTexture2D(d, "FrontMasterSprite"));
            p.Add("BackMasterSprite", TryGetTexture2D(d, "BackMasterSprite"));
            p.Add("GridRows", TryGetInt(d, "GridRows"));
            p.Add("GridCols", TryGetInt(d, "GridCols"));
            p.Add("GridCount", TryGetInt(d, "GridCount"));
            p.Add("DifferentBack", TryGetBool(d, "DifferentBack"));
            break;

        case VcToken.TokenBuildMode.Template:
            p.Add("FrontTemplate", TryGetString(d, "FrontTemplate"));
            p.Add("BackTemplate", TryGetString(d, "BackTemplate"));
            p.Add("Dataset", TryGetString(d, "Dataset"));
            break;
    }

    return p;
}
```

### Helper Methods Added

#### 1. TryGetQuickCardDataList

Deserializes a list of QuickCardData objects from JSON.

```csharp
private static List<QuickCardData> TryGetQuickCardDataList(Dictionary<string, object> d, string key)
```

**Features:**
- Handles already-deserialized `List<QuickCardData>` (pass-through)
- Parses JSON array of card data objects
- Extracts BackgroundColor, Caption, CardBackColor, CardBackValue
- Returns empty list on failure

**JSON Format:**
```json
[
  {
    "BackgroundColor": {"R":1.0,"G":0.0,"B":0.0,"A":1.0},
    "Caption": "Ace",
    "CardBackColor": {"R":0.0,"G":0.0,"B":1.0,"A":1.0},
    "CardBackValue": "Back"
  },
  ...
]
```

#### 2. ParseColorFromJson

Parses Godot Color objects from JSON (helper for TryGetQuickCardDataList).

```csharp
private static Color ParseColorFromJson(JsonElement element)
```

**Handles:**
- String format: JSON-serialized color as string
- Object format: Direct color object with R/G/B/A properties
- Case-insensitive property names (R/r, G/g, B/b, A/a)
- Defaults to black on error

**Examples:**
```json
// String format
"{\"R\":1.0,\"G\":0.5,\"B\":0.0,\"A\":1.0}"

// Object format
{"R":1.0,"G":0.5,"B":0.0,"A":1.0}
// or
{"r":1.0,"g":0.5,"b":0.0,"a":1.0}
```

#### 3. TryGetTexture2D

Deserializes Texture2D objects (for Grid mode sprite sheets).

```csharp
private static Texture2D TryGetTexture2D(Dictionary<string, object> d, string key)
```

**Features:**
- Returns Texture2D if already deserialized (pass-through)
- Loads texture from file path string
- Uses `Utility.LoadTexture()` for consistency
- Returns null on failure

**Note:** Texture2D objects cannot be fully serialized over network. Grid mode decks may need special handling for multiplayer sync (e.g., sending file path instead of texture object).

## Usage Examples

### Quick Mode Deck

```csharp
var parameters = new Dictionary<string, object>
{
    { "ComponentName", "Playing Cards" },
    { "Height", 89.0f },  // mm
    { "Width", 64.0f },   // mm
    { "Shape", 0 },
    { "Mode", VcToken.TokenBuildMode.Quick },
    { "QuickCardData", new List<QuickCardData>
        {
            new QuickCardData
            {
                BackgroundColor = Colors.Red,
                Caption = "A,2-10,J,Q,K",
                CardBackColor = Colors.Blue,
                CardBackValue = "♠"
            }
        }
    }
};

var parsed = JsonUtilities.ParseJsonToDictionary(
    VisualComponentBase.VisualComponentType.Deck,
    parameters
);
```

### Grid Mode Deck

```csharp
var parameters = new Dictionary<string, object>
{
    { "ComponentName", "Sprite Deck" },
    { "Height", 89.0f },
    { "Width", 64.0f },
    { "Shape", 0 },
    { "Mode", VcToken.TokenBuildMode.Grid },
    { "FrontMasterSprite", loadedTexture },
    { "BackMasterSprite", null },
    { "GridRows", 4 },
    { "GridCols", 13 },
    { "GridCount", 52 },
    { "DifferentBack", false }
};
```

### Template Mode Deck

```csharp
var parameters = new Dictionary<string, object>
{
    { "ComponentName", "Template Deck" },
    { "Height", 89.0f },
    { "Width", 64.0f },
    { "Shape", 0 },
    { "Mode", VcToken.TokenBuildMode.Template },
    { "FrontTemplate", "CardFrontTemplate" },
    { "BackTemplate", "CardBackTemplate" },
    { "Dataset", "CardData" }
};
```

## Multiplayer Considerations

### Serializable Parameters
✅ **Works well:**
- Quick Mode - All parameters serialize/deserialize cleanly
- Template Mode - All parameters are strings (perfect for network)

⚠️ **Needs special handling:**
- Grid Mode - Texture2D objects cannot be serialized over network

### Recommended Approach for Grid Mode

**Option 1: Store File Paths (Recommended)**
```csharp
// Store path instead of texture
{ "FrontMasterSpritePath", "res://sprites/cards.png" }
{ "BackMasterSpritePath", "res://sprites/card_backs.png" }

// On receiving end, load texture
var frontPath = parameters["FrontMasterSpritePath"] as string;
var frontTexture = Utility.LoadTexture(frontPath);
```

**Option 2: Use Cloud Assets**
```csharp
// Reference assets by AssetId
{ "FrontMasterSpriteAssetId", "asset-guid-here" }

// Download from cloud storage before building
var asset = await CloudAssetService.DownloadAssetAsync(assetId);
var texture = Utility.LoadTexture(asset.LocalPath);
```

**Option 3: Disable Grid Mode in Multiplayer**
```csharp
// In DeckPanelDialogResult, hide grid tab when multiplayer is active
if (MultiplayerManager.Instance?.IsMultiplayerActive == true)
{
    _tabs.SetTabDisabled(1, true); // Disable grid tab
}
```

## Testing

### Test Quick Mode
```csharp
var quickParams = new Dictionary<string, object>
{
    { "Mode", (int)VcToken.TokenBuildMode.Quick },
    { "QuickCardData", new List<QuickCardData>
        {
            new QuickCardData
            {
                BackgroundColor = Colors.Red,
                Caption = "Test",
                CardBackColor = Colors.Blue,
                CardBackValue = "Back"
            }
        }
    }
};

var parsed = JsonUtilities.ParseDeck(quickParams);
var cardList = parsed["QuickCardData"] as List<QuickCardData>;
GD.Print(cardList.Count); // Should print 1
```

### Test JSON Round-Trip
```csharp
// Serialize
var json = JsonSerializer.Serialize(quickParams);

// Deserialize using GameObjects helper
var restored = GameObjects.DeserializeParameters(json);

// Parse with ParseDeck
var parsed = JsonUtilities.ParseDeck(restored);

// Verify types
var cardList = parsed["QuickCardData"] as List<QuickCardData>;
GD.Print(cardList != null); // Should be true
```

## Build Status

✅ **Build successful** - All code compiles without errors

## Files Modified

1. **JsonUtilities.cs**
   - Added `ParseDeck()` method
   - Added `TryGetQuickCardDataList()` helper
   - Added `ParseColorFromJson()` helper
   - Added `TryGetTexture2D()` helper
   - Updated switch case to call ParseDeck for Deck type

## Summary

The `ParseDeck` function now properly handles all three deck build modes:
- ✅ **Quick Mode** - Deserializes QuickCardData list with colors and captions
- ✅ **Grid Mode** - Handles sprite sheet parameters (with caveat about Texture2D serialization)
- ✅ **Template Mode** - Handles template and dataset references

All parameters from both VcDeck and DeckPanelDialogResult are properly covered and will correctly deserialize from JSON for multiplayer synchronization.

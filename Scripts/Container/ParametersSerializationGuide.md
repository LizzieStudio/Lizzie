# Parameters Serialization Guide

## Overview

This document describes the helper methods for serializing and deserializing the `Parameters` dictionary (`Dictionary<string, object>`) used in `GameObjects.cs` for multiplayer synchronization.

## The Problem

When using `System.Text.Json` to serialize/deserialize `Dictionary<string, object>`:
- **Serialization works fine** - converts C# objects to JSON
- **Deserialization has type issues** - values come back as `JsonElement` instead of the original types (string, int, float, etc.)

## The Solution

Created helper methods that properly handle type conversion during deserialization.

## Methods

### 1. SerializeParameters

```csharp
private static string SerializeParameters(Dictionary<string, object> parameters)
```

**Purpose:** Convert a Parameters dictionary to JSON string.

**Parameters:**
- `parameters` - The dictionary to serialize

**Returns:** JSON string representation

**Example:**
```csharp
var parameters = new Dictionary<string, object>
{
    { "Width", 5.0 },
    { "Height", 10.0 },
    { "ComponentName", "MyToken" },
    { "IsActive", true }
};

string json = SerializeParameters(parameters);
// Result: {"Width":5.0,"Height":10.0,"ComponentName":"MyToken","IsActive":true}
```

### 2. DeserializeParameters

```csharp
private static Dictionary<string, object> DeserializeParameters(string json)
```

**Purpose:** Convert JSON string back to Parameters dictionary with proper type conversion.

**Parameters:**
- `json` - JSON string to deserialize

**Returns:** `Dictionary<string, object>` with properly typed values

**Example:**
```csharp
string json = "{\"Width\":5,\"Height\":10,\"ComponentName\":\"MyToken\",\"IsActive\":true}";

var parameters = DeserializeParameters(json);
// Result:
// parameters["Width"] is int (5)
// parameters["Height"] is int (10)
// parameters["ComponentName"] is string ("MyToken")
// parameters["IsActive"] is bool (true)
```

### 3. ConvertJsonElement (Helper)

```csharp
private static object ConvertJsonElement(JsonElement element)
```

**Purpose:** Convert a single `JsonElement` to the appropriate .NET type.

**Handles:**
- String → `string`
- Number → `int`, `long`, or `double` (tries int first, then long, then double)
- Boolean → `bool`
- Null → `null`
- Array → `List<object>`
- Object → `Dictionary<string, object>`

### 4. ConvertJsonArray (Helper)

```csharp
private static object ConvertJsonArray(JsonElement element)
```

**Purpose:** Convert a JSON array to `List<object>`.

**Example:**
```csharp
// JSON: [1, 2, 3, "test"]
// Result: List<object> { 1, 2, 3, "test" }
```

### 5. ConvertJsonObject (Helper)

```csharp
private static object ConvertJsonObject(JsonElement element)
```

**Purpose:** Convert a nested JSON object to `Dictionary<string, object>`.

**Example:**
```csharp
// JSON: { "nested": { "value": 42 } }
// Result: Dictionary with nested Dictionary
```

## Type Conversion Rules

| JSON Type | C# Type | Notes |
|-----------|---------|-------|
| string | `string` | Direct conversion |
| number (integer) | `int` | If fits in int32 range |
| number (large int) | `long` | If larger than int32 |
| number (decimal) | `double` | If has decimal point |
| true/false | `bool` | Direct conversion |
| null | `null` | Null reference |
| array | `List<object>` | Recursive conversion of elements |
| object | `Dictionary<string, object>` | Recursive conversion of properties |

## Usage in Multiplayer

### Server Side (Sending)

```csharp
public void SyncCreation(VisualComponentBase component)
{
    // Serialize component parameters
    var parametersJson = SerializeParameters(component.Parameters);
    
    // Send via RPC
    Rpc(nameof(ClientSpawnObject),
        component.GetPath(),
        (int)component.ComponentType,
        parametersJson,
        component.PrototypeRef.ToString(),
        component.Position,
        component.Rotation,
        component.ZOrder);
}
```

### Client Side (Receiving)

```csharp
[Rpc(MultiplayerApi.RpcMode.Authority)]
private void ClientSpawnObject(
    NodePath componentPath,
    int componentType,
    string parametersJson,
    string prototypeRefStr,
    Vector3 position,
    Vector3 rotation,
    int zOrder)
{
    // Deserialize parameters with proper types
    var param = DeserializeParameters(parametersJson);
    
    // Use parameters to build component
    vcb.Build(param, TextureFactory);
}
```

## Common Parameter Types

Based on the codebase, Parameters typically contain:

```csharp
new Dictionary<string, object>
{
    // Basic properties
    { "ComponentName", "TokenName" },          // string
    { "Width", 5.0 },                          // float/double
    { "Height", 10.0 },                        // float/double
    { "Diameter", 3.0 },                       // float/double
    
    // References
    { "FrontImage", "path/to/image.png" },     // string
    { "BackImage", "path/to/image.png" },      // string
    { "TemplateRef", "TemplateName" },         // string
    { "DataSetRef", "DataSetName" },           // string
    
    // Complex types (nested dictionaries or lists)
    { "QuickTextureField", new Dictionary<...> },  // Dictionary<string, object>
    { "ColoredFaces", new List<...> }              // List<object>
}
```

## Testing

To verify serialization/deserialization works correctly:

```csharp
// Test round-trip conversion
var original = new Dictionary<string, object>
{
    { "IntValue", 42 },
    { "DoubleValue", 3.14 },
    { "StringValue", "Hello" },
    { "BoolValue", true },
    { "NestedDict", new Dictionary<string, object> { { "Key", "Value" } } },
    { "Array", new List<object> { 1, 2, 3 } }
};

string json = SerializeParameters(original);
var restored = DeserializeParameters(json);

// Verify types
GD.Print(restored["IntValue"] is int);        // true
GD.Print(restored["DoubleValue"] is double);  // true
GD.Print(restored["StringValue"] is string);  // true
GD.Print(restored["BoolValue"] is bool);      // true
```

## Performance Considerations

**Serialization:**
- Fast - direct JSON conversion
- Compact output (WriteIndented = false)

**Deserialization:**
- Slightly slower due to type conversion
- Creates intermediate JsonElement objects
- Recursively processes nested structures

**Recommendations:**
- Use for network sync (acceptable overhead)
- Cache serialized results if sending to multiple clients
- Avoid serializing very large or deeply nested dictionaries

## Troubleshooting

**Issue:** Values coming back as wrong type
- **Solution:** Check the ConvertJsonElement logic - may need to handle additional type cases

**Issue:** Nested objects not deserializing correctly
- **Solution:** Ensure ConvertJsonObject is being called recursively

**Issue:** Arrays not working
- **Solution:** Verify the original Parameters use `List<object>` not arrays

**Issue:** Guids not deserializing
- **Solution:** Guids are serialized as strings and need manual parsing:
  ```csharp
  var guidStr = parameters["PrototypeRef"] as string;
  var guid = Guid.Parse(guidStr);
  ```

## Future Enhancements

Potential improvements:
- Support for more complex types (Vector2, Vector3, Color, etc.)
- Custom type converters for game-specific classes
- Validation of required parameter keys
- Schema validation for parameter dictionaries
- Compressed serialization for very large parameter sets

---

**Note:** These methods are marked `private static` and are intended for internal use within `GameObjects.cs`. If you need to serialize/deserialize Parameters elsewhere, consider moving these methods to a `ParametersSerializer` utility class.

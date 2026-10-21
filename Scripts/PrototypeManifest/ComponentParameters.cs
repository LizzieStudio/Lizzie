using System.Text.Json.Serialization;

namespace Lizzie;

[JsonPolymorphic]
public abstract record ComponentParameters
{
    public string ComponentName { get; init; } = "";
    public string BaseName { get; init; } = "";

    [JsonIgnore]
    public abstract VisualComponentBase.VisualComponentType ComponentType { get; }
}

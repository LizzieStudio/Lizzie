using System.Text.Json.Serialization;

namespace Lizzie;

[JsonPolymorphic]
public abstract record ComponentParameters
{
    [JsonIgnore]
    public abstract VisualComponentBase.VisualComponentType ComponentType { get; }
}

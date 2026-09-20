using System;
using System.Reflection;
using System.Text.Json.Serialization;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CubeParameters), (int)VisualComponentBase.VisualComponentType.Cube)]
[JsonDerivedType(typeof(DiscParameters), (int)VisualComponentBase.VisualComponentType.Disc)]
[JsonDerivedType(typeof(TokenParameters), (int)VisualComponentBase.VisualComponentType.Token)]
[JsonDerivedType(typeof(DeckParameters), (int)VisualComponentBase.VisualComponentType.Deck)]
[JsonDerivedType(typeof(DieParameters), (int)VisualComponentBase.VisualComponentType.Die)]
[JsonDerivedType(typeof(MeepleParameters), (int)VisualComponentBase.VisualComponentType.Meeple)]
[JsonDerivedType(typeof(TrayParameters), (int)VisualComponentBase.VisualComponentType.Tray)]
[JsonDerivedType(typeof(BagParameters), (int)VisualComponentBase.VisualComponentType.Bag)]
[JsonDerivedType(typeof(ZoneParameters), (int)VisualComponentBase.VisualComponentType.Zone)]
public abstract record ComponentParameters
{
    public string ComponentName { get; init; } = "";
    public string BaseName { get; init; } = "";

    [JsonIgnore]
    public abstract VisualComponentBase.VisualComponentType ComponentType { get; }

    /// <summary>
    /// This is only used so that PrintedPanelDialogResults can convert between decks and single tokens.
    /// In the future, we can consider defining decks purely by their tokens.
    /// </summary>
    public TTarget CloneAs<TTarget>()
        where TTarget : ComponentParameters, new()
    {
        var target = new TTarget();
        CopyPropertiesTo(target);
        return target;
    }

    private void CopyPropertiesTo(ComponentParameters target)
    {
        foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.Name == nameof(ComponentType))
                continue;
            var dest = target.GetType().GetProperty(prop.Name);
            if (dest != null && dest.CanWrite)
                dest.SetValue(target, prop.GetValue(this));
        }
    }
}

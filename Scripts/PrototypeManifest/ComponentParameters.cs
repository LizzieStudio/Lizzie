using System;
using System.Reflection;
using System.Text.Json.Serialization;

[JsonPolymorphic]
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

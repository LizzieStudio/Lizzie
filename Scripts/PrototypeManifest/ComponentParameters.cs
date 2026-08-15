using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using Godot;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CubeParameters), (int)VisualComponentBase.VisualComponentType.Cube)]
[JsonDerivedType(typeof(DiscParameters), (int)VisualComponentBase.VisualComponentType.Disc)]
[JsonDerivedType(typeof(TokenParameters), (int)VisualComponentBase.VisualComponentType.Token)]
[JsonDerivedType(typeof(DeckParameters), (int)VisualComponentBase.VisualComponentType.Deck)]
[JsonDerivedType(typeof(DieParameters), (int)VisualComponentBase.VisualComponentType.Die)]
[JsonDerivedType(typeof(MeshParameters), (int)VisualComponentBase.VisualComponentType.Mesh)]
[JsonDerivedType(typeof(MeepleParameters), (int)VisualComponentBase.VisualComponentType.Meeple)]
[JsonDerivedType(typeof(TrayParameters), (int)VisualComponentBase.VisualComponentType.Tray)]
[JsonDerivedType(typeof(BagParameters), (int)VisualComponentBase.VisualComponentType.Bag)]
[JsonDerivedType(typeof(ZoneParameters), (int)VisualComponentBase.VisualComponentType.Zone)]
public abstract class ComponentParameters
{
    public string ComponentName { get; set; } = "";
    public string BaseName { get; set; } = "";

    [JsonIgnore]
    public abstract VisualComponentBase.VisualComponentType ComponentType { get; }

    public ComponentParameters Clone() => (ComponentParameters)MemberwiseClone();

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

public sealed class CubeParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Cube;

    public float Height { get; set; }
    public float Width { get; set; }
    public float Length { get; set; }
    public Color Color { get; set; } = Colors.Black;
}

public sealed class DiscParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Disc;

    public float Height { get; set; }
    public float Diameter { get; set; }
    public Color Color { get; set; } = Colors.Black;
}

public sealed class BagParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Bag;

    public float Height { get; set; }
    public float Diameter { get; set; }
    public Color Color { get; set; } = Colors.Black;
    public bool ShowCount { get; set; }
}

public sealed class MeepleParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Meeple;

    public float Height { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; } = Colors.Black;
    public bool[][] Grid { get; set; } = Array.Empty<bool[]>();
}

public sealed class TrayParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Tray;

    public float Height { get; set; }
    public float Width { get; set; }
    public float Length { get; set; }
    public Color Color { get; set; } = Colors.Black;

    public string Prototype { get; set; } = "";
}

public sealed class MeshParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Mesh;
}

public sealed class ZoneParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Zone;

    public float Width { get; set; } = 2f;
    public float Depth { get; set; } = 2f;
    public bool DefaultIncluded { get; set; }
    public bool HiddenWhenExcluded { get; set; }
    public List<int> IncludedSeats { get; set; } = new();
    public List<int> ExcludedSeats { get; set; } = new();
}

public sealed class DieParameters : ComponentParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Die;

    public float Size { get; set; }
    public Color Color { get; set; } = Colors.White;
    public QuickTextureField[] Sides { get; set; } = Array.Empty<QuickTextureField>();
    public int SideCount { get; set; }
    public VcToken.TokenBuildMode Mode { get; set; }
    public string FrontTemplate { get; set; } = "";
    public string Dataset { get; set; } = "";
}

public abstract class PrintedParameters : ComponentParameters
{
    public float Height { get; set; }
    public float Width { get; set; }
    public float Thickness { get; set; }
    public int Shape { get; set; }
    public VcToken.TokenBuildMode Mode { get; set; }
    public bool DifferentBack { get; set; }

    public string FrontImage { get; set; } = "";
    public string BackImage { get; set; } = "";

    public Color FrontBgColor { get; set; } = Colors.Black;
    public Color BackBgColor { get; set; } = Colors.Black;

    public QuickTextureField QuickFront { get; set; } = new();
    public QuickTextureField QuickBack { get; set; } = new();

    public int FrontFontSize { get; set; }
    public int BackFontSize { get; set; }

    public VcToken.TokenType Type { get; set; }

    public List<QuickCardData> QuickCardData { get; set; } = new();

    public string FrontGridImageKey { get; set; } = "";
    public string BackGridImageKey { get; set; } = "";
    public int GridRows { get; set; }
    public int GridCols { get; set; }
    public int GridCount { get; set; }
    public bool GridSingleBack { get; set; }

    public string FrontTemplate { get; set; } = "";
    public string BackTemplate { get; set; } = "";
    public string Dataset { get; set; } = "";
    public string CardReference { get; set; } = "";
}

public sealed class TokenParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Token;
}

public sealed class DeckParameters : PrintedParameters
{
    [JsonIgnore]
    public override VisualComponentBase.VisualComponentType ComponentType =>
        VisualComponentBase.VisualComponentType.Deck;
}

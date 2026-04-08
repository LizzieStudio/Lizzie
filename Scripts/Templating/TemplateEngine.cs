using System.Collections.Generic;
using Godot;
using Lizzie.Scripts.Templating;

public static class TemplateEngine
{
    public static List<TextureFactory.TextureDefinition> GenerateTextureDefinitions(
        Template template,
        TextureContext _textureContext
    )
    {
        var l = new List<TextureFactory.TextureDefinition>();

        var t = new TextureContext
        {
            DataSet = _textureContext.DataSet,
            ParentSize = _textureContext.ParentSize,
        };

        if (t.DataSet == null) //no dataset, so just return a single base texture
        {
            l.Add(GenerateTextureDefinition(template, t));
            return l;
        }

        foreach (var r in _textureContext.DataSet.Rows)
        {
            t.CurrentRowName = r.Key;
            l.Add(GenerateTextureDefinition(template, t));
        }

        return l; //new List<TextureFactory.TextureDefinition>()
    }

    public static TextureFactory.TextureDefinition GenerateTextureDefinition(
        Template template,
        TextureContext _textureContext
    )
    {
        return GenerateTextureDefinition(BuildTemplateElements(template), _textureContext);
    }

    public static TextureFactory.TextureDefinition GenerateTextureDefinition(
        List<ITemplateElement> templateElements,
        TextureContext _textureContext
    )
    {
        var td = new TextureFactory.TextureDefinition
        {
            BackgroundColor = Colors.White,
            Height = (int)_textureContext.ParentSize.Y,
            Width = (int)_textureContext.ParentSize.X,
            Shape = TextureFactory.TokenShape.Square,
        };

        foreach (var element in templateElements)
        {
            MapElementToObject(td, element, _textureContext);
        }

        return td;
    }

    private static void MapElementToObject(
        TextureFactory.TextureDefinition td,
        ITemplateElement element,
        TextureContext _textureContext
    )
    {
        foreach (var l in element.GetElementData(_textureContext))
        {
            td.Objects.Add(
                new TextureFactory.TextureObject
                {
                    Width = l.Width,
                    Height = l.Height,
                    CenterX = l.CenterX,
                    CenterY = l.CenterY,
                    Anchor = l.Anchor,
                    Multiline = true,
                    Text = l.Text,
                    ForegroundColor = l.ForegroundColor,
                    Font = new SystemFont(),
                    FontSize = l.FontSize,
                    Autosize = l.Autosize,
                    HorizontalAlignment = l.HorizontalAlignment,
                    VerticalAlignment = l.VerticalAlignment,
                    Type = l.Type,
                    Stretch = l.Stretch,
                    BackgroundColor = l.BackgroundColor,
                }
            );

            foreach (var c in element.Children)
            {
                MapElementToObject(td, c, _textureContext);
            }
        }
    }

    public static ITemplateElement BuildTemplateElement(Dictionary<string, string> parameters)
    {
        TemplateElement te;

        if (!parameters.TryGetValue("Type", out var type))
            return null;

        switch (type)
        {
            case "Text":
                te = new TextElement();
                break;
            case "Image":
                te = new ImageElement();
                break;

            default:
                return null;
        }

        te.ElementName = parameters.TryGetValue("Name", out var name) ? name : string.Empty;
        te.Id = parameters.TryGetValue("Id", out var id) ? int.Parse(id) : 0;

        foreach (var kv in parameters)
        {
            te.SetParameterValue(kv.Key, kv.Value);
        }

        return te;
    }

    public static List<ITemplateElement> BuildTemplateElements(Template template)
    {
        var l = new List<ITemplateElement>();
        foreach (var t in template.Elements)
        {
            var te = TemplateEngine.BuildTemplateElement(t);

            l.Add(te);
        }

        return l;
    }

    public static List<Dictionary<string, string>> MapTemplateElementsToProjectFormat(
        IEnumerable<ITemplateElement> elements
    )
    {
        var l = new List<Dictionary<string, string>>();
        foreach (var element in elements)
        {
            MapRecursive(element, l);
        }

        return l;
    }

    private static void MapRecursive(
        ITemplateElement element,
        List<Dictionary<string, string>> parentDictionary
    )
    {
        parentDictionary.Add(MapTemplateElementToProjectFormat(element));
        foreach (var child in element.Children)
        {
            MapRecursive(child, parentDictionary);
        }
        return;
    }

    public static Dictionary<string, string> MapTemplateElementToProjectFormat(
        ITemplateElement element
    )
    {
        var d = new Dictionary<string, string>
        {
            { "Type", element.ElementType.ToString() },
            { "Name", element.ElementName },
            { "Id", element.Id.ToString() },
            { "Parent", element.Parent.ToString() },
        };
        foreach (var p in element.Parameters)
        {
            d[p.Name] = p.Value;
        }

        return d;
    }

    public static List<QuickCardData> GenerateQuickCards(
        List<QuickCardData> quickSuitData,
        int suitCount
    )
    {
        var outCards = new List<QuickCardData>();

        for (int i = 0; i < suitCount; i++)
        {
            var values = Utility.ParseValueRanges(quickSuitData[i].Caption);

            foreach (var v in values)
            {
                var c = new QuickCardData
                {
                    BackgroundColor = quickSuitData[i].BackgroundColor,
                    Caption = v,
                    CardBackColor = quickSuitData[i].CardBackColor,
                    CardBackValue = quickSuitData[i].CardBackValue,
                };

                outCards.Add(c);
            }
        }

        return outCards;
    }

    public static QuickCardData GenerateQuickCardByRow(
        List<QuickCardData> quickSuitData,
        int suitCount,
        int row
    )
    {
        int curRow = 1;

        for (int i = 0; i < suitCount; i++)
        {
            var values = Utility.ParseValueRanges(quickSuitData[i].Caption);

            foreach (var v in values)
            {
                var c = new QuickCardData
                {
                    BackgroundColor = quickSuitData[i].BackgroundColor,
                    Caption = v,
                    CardBackColor = quickSuitData[i].CardBackColor,
                    CardBackValue = quickSuitData[i].CardBackValue,
                };

                if (curRow == row)
                    return c;

                curRow++;
            }
        }

        return new QuickCardData();
    }
}

using System;
using System.Collections.Generic;
using Godot;
using Lizzie.Scripts.Templating;

public class TrackElement : TemplateElement
{
    public enum TrackTypeEnum
    {
        Horizontal,
        Vertical,
        Perimeter,
        Grid,
    }

    // Called when the node enters the scene tree for the first time.
    public TrackElement()
        : base()
    {
        ElementType = ITemplateElement.TemplateElementType.Track;

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Track Type",
                Value = nameof(TrackTypeEnum.Vertical),
                Type = TemplateParameter.TemplateParameterType.TrackType,
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Stroke Color",
                Value = (Colors.Black).ToHtml(),
                Type = TemplateParameter.TemplateParameterType.Color,
            }
        );
        Parameters.Add(
            new TemplateParameter
            {
                Name = "Stroke Width",
                Value = "2",
                Type = TemplateParameter.TemplateParameterType.Number,
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Background Color",
                Value = (Colors.Transparent).ToHtml(),
                Type = TemplateParameter.TemplateParameterType.Color,
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "# of Boxes",
                Value = "4",
                Type = TemplateParameter.TemplateParameterType.Number,
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Box Text",
                Value = string.Empty,
                Type = TemplateParameter.TemplateParameterType.Text,
            }
        );

        Parameters.Add(
            new TemplateParameter
            {
                Name = "Text Color",
                Value = (Colors.Black).ToHtml(),
                Type = TemplateParameter.TemplateParameterType.Color,
            }
        );

        UpdateBounds();
    }

    private void UpdateBounds()
    {
        SetParameterValue("Width", "100");
        SetParameterValue("Height", "100");
        SetParameterValue("X", "70");
        SetParameterValue("Y", "70");
    }

    public override List<TextureFactory.TextureObject> GetElementData(TextureContext context)
    {
        var l = new List<TextureFactory.TextureObject>();

        var boxValues = Utility.ParseValueRanges(
            EvaluateTextParameter(Parameters, "Box Text", context)
        );
        var spaceValue = EvaluateNumberParameter(Parameters, "# of Boxes", context);

        //the number of spaces on the track is the greater of the defined contents or just the number entered by the user
        var spaceCount = Math.Max(boxValues.Length, spaceValue);

        var foregroundColor = EvaluateColorParameter(Parameters, "Stroke Color", context);
        var fontColor = EvaluateColorParameter(Parameters, "Text Color", context);
        var strokeWidth = EvaluateNumberParameter(Parameters, "Stroke Width", context);
        var backgroundColor = EvaluateColorParameter(Parameters, "Background Color", context);

        var centerX = EvaluateNumberParameter(Parameters, "X", context);
        var centerY = EvaluateNumberParameter(Parameters, "Y", context);
        var height = EvaluateNumberParameter(Parameters, "Height", context);
        var width = EvaluateNumberParameter(Parameters, "Width", context);

        if (height == 0 || width == 0)
        {
            return l; //don't render if there's no size
        }

        var tt = EvaluateTrackParameter(Parameters, "Track Type", context);

        if (tt == TrackTypeEnum.Perimeter)
        {
            spaceCount = Math.Max(8, spaceCount); //for a perimeter track the minimum number of spaces is eight.
            if (spaceCount % 2 == 1)
                spaceCount++; //if it's an odd number go up by one
        }
        else
        {
            spaceCount = Math.Max(1, spaceCount); //Make sure there's at least one space. Maybe just don't render instead?
        }

        var spaces = new List<SpaceDef>();

        var sH = height / spaceCount;
        var sW = width / spaceCount;

        if (tt == TrackTypeEnum.Vertical)
        {
            for (var cnt = 0; cnt < spaceCount; cnt++)
            {
                var sd = new SpaceDef
                {
                    CenterX = centerX,
                    CenterY = centerY + (sH * (spaceCount - 1) / 2) - (sH * cnt),
                    Width = width,
                    Height = sH,
                };
                if (boxValues.Length > cnt)
                {
                    sd.Contents = boxValues[cnt];
                }

                spaces.Add(sd);
            }
        }
        else if (tt == TrackTypeEnum.Horizontal)
        {
            for (var cnt = 0; cnt < spaceCount; cnt++)
            {
                var sd = new SpaceDef
                {
                    CenterX = centerX - (sW * (spaceCount - 1) / 2) + (sW * cnt),
                    CenterY = centerY,
                    Width = sW,
                    Height = height,
                };
                if (boxValues.Length > cnt)
                {
                    sd.Contents = boxValues[cnt];
                }

                spaces.Add(sd);
            }
        }
        else if (tt == TrackTypeEnum.Grid)
        {
            //get factors
            var factors = Utility.FactorPairs(spaceCount);

            if (factors.Length > 0)
            {
                bool wide = true;
                float ratio = (float)height / width;

                if (ratio > 1)
                {
                    wide = false;
                    ratio = (float)width / height;
                }

                int f1 = 0;
                int f2 = 0;

                //find the factor pair closest to the ratio of the track
                foreach (var f in factors)
                {
                    if (ratio < f.ratio)
                    {
                        f1 = f.f1;
                        f2 = f.f2;
                        break;
                    }
                }

                if (f1 == 0)
                {
                    f1 = factors[factors.Length - 1].f1;
                    f2 = factors[factors.Length - 1].f2;
                }

                int w = 0;
                int h = 0;

                //now we have the factors and we can generate the boxes
                if (wide)
                {
                    w = f2;
                    h = f1;
                    sW = width / f2;
                    sH = height / f1;
                }
                else
                {
                    w = f1;
                    h = f2;
                    sW = width / f1;
                    sH = height / f2;
                }

                for (int i = 0; i < w; i++)
                {
                    for (int j = 0; j < h; j++)
                    {
                        var sd = new SpaceDef
                        {
                            CenterX = centerX - (width / 2) + (sW / 2) + (sW * i),
                            CenterY = centerY + (height / 2) - (sH / 2) - (sH * j),
                            Width = sW,
                            Height = sH,
                        };
                        int cnt = j * w + i;
                        if (boxValues.Length > cnt)
                        {
                            sd.Contents = boxValues[cnt];
                        }

                        spaces.Add(sd);
                    }
                }
            }
        }
        else
        {
            //perimeter track

            double halfSize = height + width;

            //calculate cell size
            var cellSize = (halfSize) / (spaceCount + 2);

            //distribute cells per side
            var wCount = (int)Math.Round((double)((spaceCount + 1) * width) / halfSize / 2f);
            var hCount = (int)Math.Round((double)((spaceCount + 1) * height) / halfSize / 2f);

            //make sure the count is right
            if (wCount + hCount < (spaceCount) / 2)
            {
                if (width > height)
                {
                    wCount++;
                }
                else
                {
                    hCount++;
                }
            }

            if (wCount + hCount > (spaceCount) / 2)
            {
                if (width > height)
                {
                    wCount--;
                }
                else
                {
                    hCount--;
                }
            }

            //now calculate the final cell widths and heights
            int cw = width / (wCount + 1);
            int ch = height / (hCount + 1);

            //do it in four stripes, starting with the upper left (0,0)
            var cx = cw / 2;
            var cy = ch / 2;

            int cnt = 0;
            for (int i = 0; i < wCount; i++)
            {
                var sd = new SpaceDef
                {
                    CenterX = cx,
                    CenterY = cy,
                    Width = cw,
                    Height = ch,
                };
                if (boxValues.Length > cnt)
                {
                    sd.Contents = boxValues[cnt];
                }

                spaces.Add(sd);

                cx += cw;
                cnt++;
            }

            for (int i = 0; i < hCount; i++)
            {
                var sd = new SpaceDef
                {
                    CenterX = cx,
                    CenterY = cy,
                    Width = cw,
                    Height = ch,
                };
                if (boxValues.Length > cnt)
                {
                    sd.Contents = boxValues[cnt];
                }

                spaces.Add(sd);

                cy += ch;
                cnt++;
            }

            for (int i = 0; i < wCount; i++)
            {
                var sd = new SpaceDef
                {
                    CenterX = cx,
                    CenterY = cy,
                    Width = cw,
                    Height = ch,
                };
                if (boxValues.Length > cnt)
                {
                    sd.Contents = boxValues[cnt];
                }

                spaces.Add(sd);

                cx -= cw;
                cnt++;
            }

            for (int i = 0; i < hCount; i++)
            {
                var sd = new SpaceDef
                {
                    CenterX = cx,
                    CenterY = cy,
                    Width = cw,
                    Height = ch,
                };
                if (boxValues.Length > cnt)
                {
                    sd.Contents = boxValues[cnt];
                }

                spaces.Add(sd);

                cy -= ch;
                cnt++;
            }
        }

        //Add background rectangle
        var bFrame = new TextureFactory.TextureObject();
        bFrame.Type = TextureFactory.TextureObjectType.RectangleFrame;
        bFrame.CenterX = centerX;
        bFrame.CenterY = centerY;
        bFrame.Height = height;
        bFrame.Width = width;
        bFrame.ForegroundColor = foregroundColor;
        bFrame.FontSize = strokeWidth;
        bFrame.BackgroundColor = backgroundColor;

        l.Add(bFrame);

        foreach (var s in spaces)
        {
            var tFrame = new TextureFactory.TextureObject();
            tFrame.Type = TextureFactory.TextureObjectType.RectangleFrame;
            tFrame.CenterX = s.CenterX;
            tFrame.CenterY = s.CenterY;
            tFrame.Height = s.Height;
            tFrame.Width = s.Width;
            tFrame.ForegroundColor = foregroundColor;
            tFrame.FontSize = strokeWidth;

            l.Add(tFrame);

            if (string.IsNullOrWhiteSpace(s.Contents))
                continue;

            var tText = new TextureFactory.TextureObject();
            tText.Type = TextureFactory.TextureObjectType.Text;
            tText.CenterX = s.CenterX;
            tText.CenterY = s.CenterY;
            tText.Height = s.Height;
            tText.Width = s.Width;
            tText.Text = s.Contents;
            tText.HorizontalAlignment = HorizontalAlignment.Center;
            tText.VerticalAlignment = VerticalAlignment.Center;
            tText.ForegroundColor = fontColor;
            tText.Autosize = true;

            l.Add(tText);
        }

        return l;
    }

    private struct SpaceDef
    {
        public int Height;
        public int Width;
        public int CenterX;
        public int CenterY;
        public string Contents;
    }
}

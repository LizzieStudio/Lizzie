using System.Collections.Generic;
using Godot;

namespace TTSS.Scripts.Templating;





public class TextElement : TemplateElement
{


	
	
	
	// Called when the node enters the scene tree for the first time.
	public TextElement(): base()
	{
		ElementType = ITemplateElement.TemplateElementType.Text;

		Parameters.Add(new TemplateParameter{ Name = "Text", Value=string.Empty });
		Parameters.Add(new TemplateParameter
		{ 
			Name = "ForegroundColor", 
			Value = (Colors.Black).ToHtml(), 
			Type=TemplateParameter.TemplateParameterType.Color
		});
	}

	public override List<TextureFactory.TextureObject> GetElementData(TextureContext context){
	
			var l = new List<TextureFactory.TextureObject>();

			var t = new TextureFactory.TextureObject();
			
			UpdateCoreParameterData(t, context);
			t.Text = EvaluateTextParameter(Parameters, "Text", context);
			t.ForegroundColor = EvaluateColorParameter(Parameters, "ForegroundColor", context);
			
			l.Add(t);
			return l;
	}

	private static int ForceParse(string s)
	{
		if (int.TryParse(s, out var i)) return i;
		return 0;
	}
	
}

public class TextElementData
{
	public string Text { get; set; }
	public Color ForegroundColor { get; set; }
}
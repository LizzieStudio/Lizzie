using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class CommandDictionary : Dictionary<VisualCommand, ICommand>
{
	public CommandDictionary(SceneController controller)
	{
		var assys = AppDomain.CurrentDomain.GetAssemblies();
		foreach (var assembly in assys)
		{
			foreach (Type type in assembly.GetTypes())
			{
				var atts = type.GetCustomAttributes(typeof(CommandAttribute));

				if (!atts.Any()) continue;

				var cmdClass = Activator.CreateInstance(type);
				if (!(cmdClass is ICommand ic)) continue;

				foreach (var a in atts)
				{
					if (a is CommandAttribute ca)
					{
						ic.Command = ca.CommandKey;
						ic.SceneController = controller;
						Add(ic.Command, ic);
					}
				}
			}
		}
	}    
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Utility : Node
{
	public static Utility Instance { get; private set; }

	private TokenTextureSubViewport _textureCreator;
	
	public override void _Ready()
	{
		Instance = this;
		_textureCreator = GetNode<TokenTextureSubViewport>("TextureCreator");
	}

	public Texture2D CreateQuickTexture(TokenTextureParameters parameters)
	{
		return _textureCreator.CreateQuickTexture(parameters);
	}

	public float GetAabbSize(Aabb aabb)
	{
		return aabb.GetLongestAxisSize();
	}

	/// <summary>
	/// This function parses a string like "1-6, SKIP, +2" into a string array.
	/// (which in this case would be 1, 2, 3, 4, 5, 6, SKIP, +2)
	/// Also can understand single character ranges like "A-J"
	/// </summary>
	/// <param name="input">string to parse</param>
	/// <returns>resulting array</returns>
	public static string[] ParseValueRanges(string input)
	{
		if (string.IsNullOrEmpty(input)) return Array.Empty<string>();
		var sInput = input.Replace(" ", string.Empty);

		var pages = new List<string>();
		var ranges = sInput.Split(',');

		foreach (var range in ranges)
		{
			if (range.Contains('-'))
			{
				var sBound = range.Split('-');
				if (sBound.Length != 2) continue;

				if (int.TryParse(sBound[0], out var low) && int.TryParse(sBound[1], out var high)
														 && high >= low)
				{
					pages.AddRange(Enumerable.Range(low, high - low + 1).ToArray()
						.Select(s => s.ToString()));
				}
				else if (sBound[0].Length == 1 && sBound[1].Length == 1)
				{
					//ASCII single character mode
					char c1 = sBound[0][0];
					char c2 = sBound[1][0];

					if (char.IsAscii(c1) && char.IsAscii(c2))
					{
						var a1 = (int)c1;
						var a2 = (int)c2;

						if (a1 > a2) (a1, a2) = (a2, a1);

						for (int i = a1; i <= a2; i++)
						{
							pages.Add(((char)i).ToString());
						}
					}
				}

				else
				{
					//just give up and add the original string
					pages.Add(range);
				}
			}
			else
			{
				pages.Add(range);
			}
		}

		return pages.ToArray();
	}

	public static float PixelSize(Vector2 size)
	{
		if (size.X == 0 || size.Y == 0) return 0;

		return 0.95f / Mathf.Max(size.X, size.Y);
	}
	
}

using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public static class ShapeCollider
{
	public struct ColliderRectangle
	{
		public double CenterX, CenterY, Width, Height, Angle;

		public ColliderRectangle(Vector2 center, double width, double height, double angle) : this(center.X,
			center.Y, width, height, angle)
		{
		}

		public ColliderRectangle(double centerX, double centerY, double width, double height, double angle)
		{
			CenterX = centerX;
			CenterY = centerY;
			Width = width;
			Height = height;
			Angle = angle;
		}
	}

	private static double[,] GetVertices(ColliderRectangle rect)
	{
		double angle = rect.Angle * Math.PI / 180.0;
		double dx = rect.Width / 2;
		double dy = rect.Height / 2;

		double[,] corners =
		{
			{ -dx, -dy },
			{ dx, -dy },
			{ dx, dy },
			{ -dx, dy }
		};

		double cos = Math.Cos(angle);
		double sin = Math.Sin(angle);

		double[,] rotatedCorners = new double[4, 2];
		for (int i = 0; i < 4; i++)
		{
			rotatedCorners[i, 0] = corners[i, 0] * cos - corners[i, 1] * sin + rect.CenterX;
			rotatedCorners[i, 1] = corners[i, 0] * sin + corners[i, 1] * cos + rect.CenterY;
		}

		return rotatedCorners;
	}

	private static double[] Project(double[,] vertices, double[] axis)
	{
		double min = double.MaxValue;
		double max = double.MinValue;

		for (int i = 0; i < 4; i++)
		{
			double dot = vertices[i, 0] * axis[0] + vertices[i, 1] * axis[1];
			if (dot < min) min = dot;
			if (dot > max) max = dot;
		}

		return new double[] { min, max };
	}

	private static bool Overlap(double[] proj1, double[] proj2)
	{
		return !(proj1[1] < proj2[0] || proj2[1] < proj1[0]);
	}

	/// <summary>
	/// Determines if two rectangles overlaps.
	/// Rectangles may be rotated
	/// </summary>
	/// <param name="rect1">Rectangle 1</param>
	/// <param name="rect2">Rectangle 2</param>
	/// <returns>true = overlap</returns>
	public static bool DoRotatedRectanglesOverlap(ColliderRectangle rect1, ColliderRectangle rect2)
	{
		double[,] vertices1 = GetVertices(rect1);
		double[,] vertices2 = GetVertices(rect2);

		double[][] axes =
		{
			new double[] { vertices1[1, 0] - vertices1[0, 0], vertices1[1, 1] - vertices1[0, 1] },
			new double[] { vertices1[3, 0] - vertices1[0, 0], vertices1[3, 1] - vertices1[0, 1] },
			new double[] { vertices2[1, 0] - vertices2[0, 0], vertices2[1, 1] - vertices2[0, 1] },
			new double[] { vertices2[3, 0] - vertices2[0, 0], vertices2[3, 1] - vertices2[0, 1] }
		};

		foreach (var axis in axes)
		{
			double length = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1]);
			axis[0] /= length;
			axis[1] /= length;

			double[] proj1 = Project(vertices1, axis);
			double[] proj2 = Project(vertices2, axis);

			if (!Overlap(proj1, proj2))
			{
				return false;
			}
		}

		return true;
	}

	/*
	public static void Main()
	{
		ColliderRectangle rectA = new ColliderRectangle(0, 0, 2, 2, 45);
		ColliderRectangle rectB = new ColliderRectangle(1, 1, 2, 2, 45);

		Console.WriteLine(DoRotatedRectanglesOverlap(rectA, rectB)); // Output: True or False
	}
*/

	public static bool CircleRectangleOverlap(double radius, Vector2 circleCenter, Vector2 rectangleCenter, double W,
		double H,
		double theta)
	{
		return CircleRectangleOverlap(radius, circleCenter.X, circleCenter.Y, rectangleCenter.X, rectangleCenter.Y, H,
			W,
			theta);
	}

	/// <summary>
	/// Determines if a circle and rectangle overlap
	/// </summary>
	/// <param name="radius">Circle radius</param>
	/// <param name="circleX">Circle center X</param>
	/// <param name="circleY">Circle center Y</param>
	/// <param name="rectX">Rectangle center X</param>
	/// <param name="rectY">Rectangle center Y</param>
	/// <param name="W">Rectangle width</param>
	/// <param name="H">Rectangle height</param>
	/// <param name="theta">Rectangle angle</param>
	/// <returns>true = overlap</returns>
	public static bool CircleRectangleOverlap(double radius, double circleX, double circleY, double rectX, double rectY,
		double W, double H, double theta)
	{
		// Rotate the circle's center back by -theta
		double cosTheta = Math.Cos(-theta);
		double sinTheta = Math.Sin(-theta);

		double XcPrime = cosTheta * (circleX - rectX) - sinTheta * (circleY - rectY) + rectX;
		double YcPrime = sinTheta * (circleX - rectX) + cosTheta * (circleY - rectY) + rectY;

		// Define the axis-aligned rectangle's corners
		double X1 = rectX - W / 2;
		double Y1 = rectY - H / 2;
		double X2 = rectX + W / 2;
		double Y2 = rectY + H / 2;

		// Find the closest Vector2 on the rectangle to the circle's center
		double Xn = Math.Max(X1, Math.Min(XcPrime, X2));
		double Yn = Math.Max(Y1, Math.Min(YcPrime, Y2));

		// Calculate the distance from the closest Vector2 to the circle's center
		double Dx = Xn - XcPrime;
		double Dy = Yn - YcPrime;
		double distance = Math.Sqrt(Dx * Dx + Dy * Dy);

		// Check if the distance is less than or equal to the circle's radius
		return distance <= radius;
	}

	/// <summary>
	/// Checks if two circles overlap
	/// </summary>
	/// <param name="r1">Circle 1 radius</param>
	/// <param name="center1">Circle 1 center</param>
	/// <param name="r2">Circle 2 radius</param>
	/// <param name="center2">Circle 2 center</param>
	/// <returns>true = overlap</returns>
	public static bool CirclesOverlap(double r1, Vector2 center1, double r2, Vector2 center2)
	{
		//if the distance between the centers is < the sum of the radii, we collide
		return (center2 - center1).Length() < (r1 + r2);
	}

	#region Triangles

	// Function to check if two triangles overlap
	static bool TrianglesOverlap(Vector2[] tri1, Vector2[] tri2)
	{
		// Function to project a triangle onto an axis
		double[] Project(Vector2[] tri, Vector2 axis)
		{
			double min = (tri[0].X * axis.X + tri[0].Y * axis.Y);
			double max = min;
			for (int i = 1; i < 3; i++)
			{
				double projection = (tri[i].X * axis.X + tri[i].Y * axis.Y);
				if (projection < min) min = projection;
				if (projection > max) max = projection;
			}

			return new double[] { min, max };
		}

		// Function to check if projections overlap
		bool Overlap(double[] proj1, double[] proj2)
		{
			return !(proj1[1] < proj2[0] || proj2[1] < proj1[0]);
		}

		// Function to get the edge normals (potential separating axes)
		Vector2[] GetAxes(Vector2[] tri)
		{
			Vector2[] axes = new Vector2[3];
			for (int i = 0; i < 3; i++)
			{
				Vector2 p1 = tri[i];
				Vector2 p2 = tri[(i + 1) % 3];
				Vector2 edge = new Vector2(p2.X - p1.X, p2.Y - p1.Y);
				axes[i] = new Vector2(-edge.Y, edge.X); // Perpendicular vector
			}

			return axes;
		}

		// Get the axes for both triangles
		Vector2[] axes1 = GetAxes(tri1);
		Vector2[] axes2 = GetAxes(tri2);

		// Check for overlap on all axes
		foreach (Vector2 axis in axes1)
		{
			if (!Overlap(Project(tri1, axis), Project(tri2, axis)))
				return false;
		}

		foreach (Vector2 axis in axes2)
		{
			if (!Overlap(Project(tri1, axis), Project(tri2, axis)))
				return false;
		}

		return true; // No separating axis found, triangles must overlap
	}

	// Function to check if a point is inside a triangle
	public static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
	{
		double areaOrig = Math.Abs((a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y)) / 2.0);
		double area1 = Math.Abs((p.X * (b.Y - c.Y) + b.X * (c.Y - p.Y) + c.X * (p.Y - b.Y)) / 2.0);
		double area2 = Math.Abs((a.X * (p.Y - c.Y) + p.X * (c.Y - a.Y) + c.X * (a.Y - p.Y)) / 2.0);
		double area3 = Math.Abs((a.X * (b.Y - p.Y) + b.X * (p.Y - a.Y) + p.X * (a.Y - b.Y)) / 2.0);
		return (areaOrig == area1 + area2 + area3);
	}

	// Function to check if a Point is inside a polygon
	public static bool IsPointInPolygon(Vector2 p, List<Vector2> polygon)
	{
		int n = polygon.Count;
		for (int i = 0; i < n; i++)
		{
			Vector2 a = polygon[i];
			Vector2 b = polygon[(i + 1) % n];
			Vector2 c = polygon[(i + 2) % n];
			if (IsPointInTriangle(p, a, b, c))
				return true;
		}

		return false;
	}

	// Function to triangulate a polygon using the Ear Clipping method
	static List<Tuple<Vector2, Vector2, Vector2>> Triangulate(List<Vector2> polygon)
	{
		List<Tuple<Vector2, Vector2, Vector2>> triangles = new List<Tuple<Vector2, Vector2, Vector2>>();
		List<Vector2> remainingVertices = new List<Vector2>(polygon);

		while (remainingVertices.Count > 3)
		{
			bool earFound = false;
			for (int i = 0; i < remainingVertices.Count; i++)
			{
				Vector2 a = remainingVertices[i];
				Vector2 b = remainingVertices[(i + 1) % remainingVertices.Count];
				Vector2 c = remainingVertices[(i + 2) % remainingVertices.Count];

				bool isEar = true;
				for (int j = 0; j < remainingVertices.Count; j++)
				{
					if (j == i || j == (i + 1) % remainingVertices.Count || j == (i + 2) % remainingVertices.Count)
						continue;

					if (IsPointInTriangle(remainingVertices[j], a, b, c))
					{
						isEar = false;
						break;
					}
				}

				if (isEar)
				{
					triangles.Add(new Tuple<Vector2, Vector2, Vector2>(a, b, c));
					remainingVertices.RemoveAt((i + 1) % remainingVertices.Count);
					earFound = true;
					break;
				}
			}

			if (!earFound)
				//throw new Exception("No ear found. The polygon might be self-intersecting or not simple.");
				return triangles; //just work with what we have
		}

		triangles.Add(new Tuple<Vector2, Vector2, Vector2>(remainingVertices[0], remainingVertices[1],
			remainingVertices[2]));
		return triangles;
	}
	
		// Structure to represent a circle
	struct Circle
	{
		public Vector2 Center;
		public double Radius;
		public Circle(Vector2 center, double radius)
		{
			Center = center;
			Radius = radius;
		}
	}


	// Function to check if a circle intersects with a line segment
	static bool CircleIntersectsLine(Circle circle, Vector2 a, Vector2 b)
	{
		double ax = a.X - circle.Center.X;
		double ay = a.Y - circle.Center.Y;
		double bx = b.X - circle.Center.X;
		double by = b.Y - circle.Center.Y;

		double a2 = ax * ax + ay * ay;
		double b2 = bx * bx + by * by;
		double ab = ax * bx + ay * by;

		double d = a2 * b2 - ab * ab;
		double r2 = circle.Radius * circle.Radius;

		if (d > r2 * (a2 + b2 - 2 * ab))
			return false;

		double t = (ab - Math.Sqrt(r2 * (a2 + b2 - 2 * ab) - d)) / (a2 + b2 - 2 * ab);
		return t >= 0 && t <= 1;
	}

	// Function to check if a circle and a triangle overlap
	static bool CircleIntersectsTriangle(Circle circle, Vector2 a, Vector2 b, Vector2 c)
	{
		// Check if the circle's center is inside the triangle
		if (IsPointInTriangle(circle.Center, a, b, c))
			return true;

		// Check if the circle intersects any of the triangle's edges
		if (CircleIntersectsLine(circle, a, b) || CircleIntersectsLine(circle, b, c) || CircleIntersectsLine(circle, c, a))
			return true;

		return false;
	}

	#endregion
}

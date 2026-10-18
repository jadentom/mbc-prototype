using Godot;
using System.Collections.Generic;

namespace MbcPrototype.Units;

/// <summary>
/// The shape a structure of a given unit is drawn as, seen from the match's
/// tilted camera (docs/parity §2: what a unit *looks like* is data too, so a
/// Harvester and a Hub are told apart at a glance without a model file).
///
/// It is a **silhouette**, not a model: every node is a flat plate of the same
/// footprint — radius 1.0 (the node's own radius, see `Scenes/structure.tscn`)
/// and the same thickness — so nothing the rules reason about changes when a
/// unit's art does. Cord lengths, landing positions, the minimap dots and the
/// selection ring all stay a disc; only the outline differs.
/// </summary>
public enum UnitSilhouette
{
	/// <summary>
	/// Not stated by the unit's resource. A structure falls back to the disc and
	/// says so, and <see cref="UnitCatalog.Validate"/> reports it — a default must
	/// never be a real choice (docs/parity §2).
	/// </summary>
	Unset = 0,

	/// <summary>A round plate: the Hub, and the fallback for a unit that states no shape.</summary>
	Disc = 1,

	/// <summary>A three-cornered plate pointing along -Z (up on the minimap): the Harvester.</summary>
	Triangle = 2,

	/// <summary>A square plate, for a unit that should read as blocky rather than round or pointed.</summary>
	Square = 3,
}

/// <summary>
/// Builds the meshes <see cref="UnitSilhouette"/> names.
///
/// The disc is Godot's own cylinder, so a unit that wants the ordinary node shape
/// draws exactly what `Scenes/structure.tscn` authored. The others are flat
/// prisms built in code from a 2D outline (there is no triangular-plate
/// primitive), the same way <see cref="MbcPrototype.World.PurplePool"/> builds its
/// rounded square: explicit vertices and face normals, no winding to get wrong.
/// </summary>
public static class UnitSilhouettes
{
	/// <summary>
	/// Radius of every structure's footprint, in world units — the number
	/// `Scenes/structure.tscn` gives the node's mesh, its collision shape and (at
	/// 1.4×) its selection ring. A silhouette has to fit inside it.
	/// </summary>
	public const float Radius = 1.0f;

	/// <summary>
	/// Height of a node's plate. Flat: a node is a thing lying on the ground, and
	/// the cords are measured between their centres, so the thickness is looks
	/// rather than geometry.
	/// </summary>
	public const float Thickness = 0.2f;

	/// <summary>
	/// The mesh a unit's structures are drawn as. <see cref="UnitSilhouette.Unset"/>
	/// builds the disc — the shape every structure had before a unit could choose.
	/// </summary>
	public static Mesh CreateMesh(UnitSilhouette silhouette)
	{
		switch (silhouette)
		{
			case UnitSilhouette.Triangle:
				return FlatPrism(TriangleOutline());
			case UnitSilhouette.Square:
				return FlatPrism(SquareOutline());
			default:
				return Disc();
		}
	}

	/// <summary>The ordinary node: a round plate, sized to the footprint.</summary>
	private static CylinderMesh Disc()
	{
		return new CylinderMesh
		{
			TopRadius = Radius,
			BottomRadius = Radius,
			Height = Thickness,
		};
	}

	/// <summary>
	/// A three-cornered outline whose corners sit on the footprint circle, with one
	/// corner pointing along -Z: the direction the minimap draws as "up", so the
	/// triangle points the same way on the map and on the widget.
	/// </summary>
	private static Vector2[] TriangleOutline()
	{
		float half = Mathf.Sqrt(3f) * 0.5f;
		return new[]
		{
			new Vector2(0f, -Radius),
			new Vector2(Radius * half, Radius * 0.5f),
			new Vector2(-Radius * half, Radius * 0.5f),
		};
	}

	/// <summary>A square plate whose edges sit on the footprint's own half-extent.</summary>
	private static Vector2[] SquareOutline()
	{
		return new[]
		{
			new Vector2(-Radius, -Radius),
			new Vector2(Radius, -Radius),
			new Vector2(Radius, Radius),
			new Vector2(-Radius, Radius),
		};
	}

	/// <summary>
	/// Extrudes a flat outline lying in the XZ plane into a thin plate: a top face,
	/// a bottom face and one quad per edge. Normals are given per face (up, down,
	/// and straight out from the outline), so nothing depends on the triangle
	/// winding — the structure's material draws both sides anyway, because a plate
	/// this thin is seen from below when the camera looks across it.
	/// </summary>
	private static ArrayMesh FlatPrism(Vector2[] outline)
	{
		float half = Thickness * 0.5f;
		var vertices = new List<Vector3>(outline.Length * 18);
		var normals = new List<Vector3>(outline.Length * 18);

		for (int i = 0; i < outline.Length; i++)
		{
			Vector2 a = outline[i];
			Vector2 b = outline[(i + 1) % outline.Length];

			// Top face, fanned from the centre.
			AddTriangle(vertices, normals,
				new Vector3(0f, half, 0f),
				new Vector3(a.X, half, a.Y),
				new Vector3(b.X, half, b.Y),
				Vector3.Up);

			// Bottom face, the same fan facing the other way.
			AddTriangle(vertices, normals,
				new Vector3(0f, -half, 0f),
				new Vector3(b.X, -half, b.Y),
				new Vector3(a.X, -half, a.Y),
				Vector3.Down);

			// The edge, as two triangles. Its normal points away from the plate's
			// centre, which is what the middle of the edge tells us.
			Vector2 edge = b - a;
			Vector3 outward = new Vector3(edge.Y, 0f, -edge.X).Normalized();
			if (outward.X * (a.X + b.X) + outward.Z * (a.Y + b.Y) < 0f) outward = -outward;

			AddTriangle(vertices, normals,
				new Vector3(a.X, half, a.Y),
				new Vector3(b.X, half, b.Y),
				new Vector3(b.X, -half, b.Y),
				outward);
			AddTriangle(vertices, normals,
				new Vector3(a.X, half, a.Y),
				new Vector3(b.X, -half, b.Y),
				new Vector3(a.X, -half, a.Y),
				outward);
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static void AddTriangle(
		List<Vector3> vertices, List<Vector3> normals,
		Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
	{
		vertices.Add(a);
		vertices.Add(b);
		vertices.Add(c);
		normals.Add(normal);
		normals.Add(normal);
		normals.Add(normal);
	}
}

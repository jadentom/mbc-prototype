using Godot;
using System.Collections.Generic;

namespace MbcPrototype.World;

/// <summary>
/// One pool of purple energy lying flat on the ground: a solid, rounded square
/// with a rim of dead, sandy grass around it, whose colour pulses between a
/// resting violet and a bright glow.
///
/// The pool is opaque and always there — the pulse is colour, not transparency,
/// so the map reads the same at every moment and a pool is a thing you could
/// fall into rather than a tint over the grass.
///
/// Nothing in the game reacts to a pool yet. What a pool knows — where it is and
/// how wide the energy is — is exactly what a future collision check needs, so
/// its footprint is exposed as geometry (<see cref="Footprint"/>,
/// <see cref="ContainsXZ(Vector2)"/>, <see cref="OverlapsXZ"/>) instead of being
/// kept private to the drawing code. That footprint is the pool itself, not the
/// sandy rim around it. Pools are placed by <see cref="PoolField"/>.
///
/// The shape is built in code (there is no rounded-square primitive) and shared
/// by every pool — see <see cref="CreateMesh"/>.
/// </summary>
public partial class PurplePool : Node3D
{
	// --- Shape, filled in by PoolField.Spawn before Build() ---

	/// <summary>Side of the square pool, in world units.</summary>
	public float Side { get; private set; } = 6.0f;

	/// <summary>
	/// The world-space XZ rectangle the pool itself covers: the footprint a future
	/// collision check tests against, in the same corner/position form as the
	/// minimap's own world rect. The sandy rim reaches further out and is not part
	/// of it: dead grass is scenery, the energy is the hazard.
	/// </summary>
	public Rect2 Footprint => new Rect2(
		new Vector2(GlobalPosition.X, GlobalPosition.Z) - Vector2.One * (Side * 0.5f),
		Vector2.One * Side);

	/// <summary>
	/// Corner radius of the rounded square, as a fraction of the side. A quarter
	/// of the side would be a circle at the corners' expense; a fifth keeps the
	/// pool reading as a square while taking the points off.
	/// </summary>
	private const float CornerRadiusFraction = 0.2f;

	/// <summary>
	/// Render layer the pool geometry is drawn on (layer 2). It is a layer of its
	/// own so the minimap's top-down camera can leave it out: the minimap draws
	/// flat, steady-coloured pool markers instead of the pulsing 3D shapes.
	/// The main camera has to include this layer (GameManager.CreatePools does).
	/// </summary>
	public const uint WorldRenderLayer = 1u << 1;

	/// <summary>
	/// Width of the sandy rim of dead grass drawn around the pool, as a fraction
	/// of the side. Outside the pool's own footprint, so it changes the map's
	/// looks and not its geometry.
	/// </summary>
	public const float BorderWidthFraction = 0.1f;

	/// <summary>Corner rounding of a pool of this side, in world units.</summary>
	private static float CornerRadiusFor(float side) => side * CornerRadiusFraction;

	/// <summary>Width of the sandy rim for a pool of this side, in world units.</summary>
	public static float BorderWidthFor(float side) => side * BorderWidthFraction;

	/// <summary>
	/// Colour of the rim. Dry, sandy and desaturated: the grass here is dead, and
	/// a rim that kept the pool's purple would read as part of the energy. Kept
	/// constant through the pulse for the same reason — the rim is ground, so it
	/// must not appear to light up with the pool.
	/// </summary>
	private static readonly Color SandColor = new Color(0.76f, 0.69f, 0.47f);

	/// <summary>
	/// How far above the ground plane a pool is drawn: just clear of the ground
	/// mesh, and below a blast's ground marker (BlastVisual.MarkerHeight), so a
	/// pool never swallows the marker that says where a bomb landed.
	///
	/// One height for every pool is safe because the map is a fixed grid whose
	/// pitch is far larger than a pool: no two pools are ever coincident. A layout
	/// that let pools overlap would need a small per-pool spread here instead —
	/// two coplanar surfaces z-fight into a flicker.
	/// </summary>
	private const float PoolHeight = 0.02f;

	// --- The pulse: colour only, see Advance() ---

	/// <summary>Seconds the pool takes to brighten from its resting colour to full glow.</summary>
	private const float RiseSeconds = 0.8f;

	/// <summary>Seconds the pool holds at full glow.</summary>
	private const float LitSeconds = 2.0f;

	/// <summary>Seconds the pool takes to sink back to its resting colour.</summary>
	private const float FallSeconds = 1.6f;

	/// <summary>Seconds the pool rests dark before its next rise.</summary>
	private const float RestSeconds = 0.6f;

	/// <summary>The whole pulse, which every pool runs at the same length.</summary>
	private const float CycleSeconds = RiseSeconds + LitSeconds + FallSeconds + RestSeconds;

	/// <summary>
	/// Resting colour of the energy between pulses: deep violet, still clearly a
	/// pool. The pool never fades out, so this is as dark as the ground here ever
	/// gets.
	/// </summary>
	private static readonly Color DimColor = new Color(0.22f, 0.05f, 0.40f);

	/// <summary>
	/// Colour at full glow: the lit-up plateau of the pulse, and the colour the
	/// minimap draws pools in, so a pool is recognisably the same thing on both.
	/// </summary>
	public static readonly Color LitColor = new Color(0.80f, 0.44f, 1.0f);

	// The one material this pool animates. Per pool rather than shared, because
	// every pool sits at its own point in the pulse.
	private StandardMaterial3D _material;

	// Seconds since this pool was placed, including the head start that puts it
	// at its own point in the cycle: the position within the cycle is the only
	// thing the pulse reads.
	private float _clock;

	/// <summary>
	/// Places one pool centred on <paramref name="centerXZ"/> and returns it.
	/// </summary>
	/// <param name="parent">Node the pool is parented to — inside the scene, never the tree root.</param>
	/// <param name="centerXZ">Centre of the pool, in world XZ.</param>
	/// <param name="side">Side of the square pool, in world units.</param>
	/// <param name="phase01">
	/// Where in the pulse this pool starts, 0..1 of a cycle. Fixed per pool by
	/// <see cref="PoolField"/>, so the map looks the same on every run while the
	/// pools still do not pulse in unison.
	/// </param>
	/// <param name="mesh">
	/// The shared pool shape from <see cref="CreateMesh"/>. Passed in rather than
	/// built per pool: every pool on the map is the same size, and a mesh is a
	/// resource the whole map can draw from.
	/// </param>
	public static PurplePool Spawn(Node parent, Vector2 centerXZ, float side, float phase01, ArrayMesh mesh)
	{
		var pool = new PurplePool
		{
			Name = "PurplePool",
			Side = Mathf.Max(0.01f, side),
		};
		parent.AddChild(pool);

		// The spawn container sits at the origin with an identity transform, so a
		// local position here is a world one (the same assumption SpawnRoot and
		// Projectile.Deploy make).
		pool.Position = new Vector3(centerXZ.X, PoolHeight, centerXZ.Y);
		pool._clock = Mathf.PosMod(phase01, 1f) * CycleSeconds;

		pool.Build(mesh);
		return pool;
	}

	/// <summary>
	/// Builds the mesh every pool of this size draws: one flat, rounded square
	/// made of two surfaces —
	///
	/// <list type="bullet">
	/// <item>surface 0, the rim of dead grass, which carries its own fixed sandy
	/// material because it never changes;</item>
	/// <item>surface 1, the pool itself, left without a material so each pool can
	/// override it with the one its pulse animates.</item>
	/// </list>
	///
	/// Both outlines are rounded rectangles sampled at matching points, so the rim
	/// is a constant width all the way round, corners included.
	/// </summary>
	/// <param name="side">Side of the square pool, in world units.</param>
	public static ArrayMesh CreateMesh(float side)
	{
		float half = Mathf.Max(0.01f, side * 0.5f);
		float radius = Mathf.Min(CornerRadiusFor(side), half);
		float border = BorderWidthFor(side);

		Vector2[] pool = RoundedOutline(half, radius);
		Vector2[] rim = RoundedOutline(half + border, radius + border);

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, FlatSurface(RimTriangles(pool, rim)));
		mesh.SurfaceSetMaterial(0, SandMaterial());
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, FlatSurface(PoolTriangles(pool)));
		return mesh;
	}

	/// <summary>
	/// The outline of a rounded rectangle, anticlockwise in the XZ plane: the
	/// straight edges fall out of the gaps between one corner's arc and the next.
	/// </summary>
	/// <param name="halfExtent">Half the side of the square.</param>
	/// <param name="radius">Corner radius; clamped to the square's own half extent.</param>
	private static Vector2[] RoundedOutline(float halfExtent, float radius)
	{
		const int SegmentsPerCorner = 6;

		float r = Mathf.Clamp(radius, 0.0001f, halfExtent);
		float center = halfExtent - r;

		// Corner centres anticlockwise from +X/+Z, the order the arcs are walked
		// in so the outline never crosses itself.
		var centers = new[]
		{
			new Vector2(center, center),
			new Vector2(-center, center),
			new Vector2(-center, -center),
			new Vector2(center, -center),
		};

		var points = new Vector2[SegmentsPerCorner * centers.Length];
		int index = 0;
		for (int corner = 0; corner < centers.Length; corner++)
		{
			// Each corner spans a quarter turn. The first sample sits at the start
			// of the arc and the last one step short of the next corner, so the
			// outline closes without repeating a point.
			float start = Mathf.Pi * 0.5f * corner;
			for (int step = 0; step < SegmentsPerCorner; step++)
			{
				float angle = start + Mathf.Pi * 0.5f * step / SegmentsPerCorner;
				points[index++] = centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
			}
		}

		return points;
	}

	/// <summary>The pool itself: a triangle fan from its centre out to its outline.</summary>
	private static List<Vector2> PoolTriangles(Vector2[] outline)
	{
		var triangles = new List<Vector2>(outline.Length * 3);
		for (int i = 0; i < outline.Length; i++)
		{
			triangles.Add(Vector2.Zero);
			triangles.Add(outline[i]);
			triangles.Add(outline[(i + 1) % outline.Length]);
		}
		return triangles;
	}

	/// <summary>
	/// The rim: the band between the pool's outline and the larger outline around
	/// it, as a quad per pair of matching points.
	/// </summary>
	private static List<Vector2> RimTriangles(Vector2[] inner, Vector2[] outer)
	{
		var triangles = new List<Vector2>(inner.Length * 6);
		for (int i = 0; i < inner.Length; i++)
		{
			int next = (i + 1) % inner.Length;
			triangles.Add(inner[i]);
			triangles.Add(outer[i]);
			triangles.Add(inner[next]);

			triangles.Add(inner[next]);
			triangles.Add(outer[i]);
			triangles.Add(outer[next]);
		}
		return triangles;
	}

	/// <summary>
	/// Turns XZ outline points into a mesh surface lying flat on the ground.
	///
	/// No winding care is needed: both materials are double-sided, and a flat quad
	/// on the ground is only ever seen from above.
	/// </summary>
	private static Godot.Collections.Array FlatSurface(List<Vector2> points)
	{
		var vertices = new Vector3[points.Count];
		var normals = new Vector3[points.Count];
		for (int i = 0; i < points.Count; i++)
		{
			vertices[i] = new Vector3(points[i].X, 0f, points[i].Y);
			normals[i] = Vector3.Up;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		return arrays;
	}

	/// <summary>
	/// Material of the dead-grass rim: flat and unlit, like the ground itself
	/// (Scenes/main_scene.tscn uses an unshaded grass material), so the rim sits
	/// on the ground instead of looking like a lit surface laid over it.
	/// </summary>
	private static StandardMaterial3D SandMaterial()
	{
		return new StandardMaterial3D
		{
			AlbedoColor = SandColor,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			// Drawn from both sides, so nothing disappears when the camera is
			// tilted low and looks at the ground edge-on.
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	/// <summary>
	/// Adds the shared pool shape, and the material this pool pulses. Nothing here
	/// knows about the pulse — <see cref="Advance"/> drives that material every
	/// frame.
	/// </summary>
	private void Build(ArrayMesh mesh)
	{
		_material = new StandardMaterial3D
		{
			// Starts at the resting colour: the first frame is the start of a rise.
			AlbedoColor = DimColor,
			// Unshaded: a pool is a light source sitting on the ground, not a
			// surface the sun happens to hit. It is also the affordable way to read
			// as a glow — an OmniLight3D per pool would be hundreds of lights in
			// the forward renderer, and glow post-processing needs a
			// WorldEnvironment the project does not have. Brightening the albedo is
			// what "lighting up" means here.
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		var visual = new MeshInstance3D
		{
			Name = "PoolMesh",
			Mesh = mesh,
			// On the pools' own layer: the main camera draws it, the minimap's
			// camera does not (it draws flat markers instead).
			Layers = WorldRenderLayer,
			// A flat shape lying on the ground has no shadow worth casting, and
			// hundreds of them casting one would be pure cost.
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		// Surface 0 keeps the mesh's own sandy material; surface 1 gets this pool's
		// pulsing one.
		visual.SetSurfaceOverrideMaterial(1, _material);
		AddChild(visual);
	}

	/// <summary>
	/// Moves this pool's pulse on by <paramref name="delta"/> seconds and applies
	/// it. Driven by <see cref="PoolField"/>, which owns the field's single
	/// per-frame loop.
	/// </summary>
	/// <param name="delta">Seconds since the last frame.</param>
	public void Advance(float delta)
	{
		if (_material == null) return;

		_clock += delta;
		float glow = GlowAt(Mathf.PosMod(_clock, CycleSeconds));

		// Colour only, and nothing else: the pool stays solid, so brightening the
		// energy is the whole effect. The rim is a separate surface and does not
		// move with it.
		_material.AlbedoColor = DimColor.Lerp(LitColor, glow);
	}

	/// <summary>
	/// The pulse itself: brighten, hold at full glow, sink back, rest, repeat.
	/// </summary>
	/// <param name="inCycle">Seconds into the cycle.</param>
	/// <returns>0 (resting colour) to 1 (fully lit), eased at both ends of each ramp.</returns>
	private static float GlowAt(float inCycle)
	{
		if (inCycle < RiseSeconds)
		{
			return Ease(inCycle / RiseSeconds);
		}
		inCycle -= RiseSeconds;

		if (inCycle < LitSeconds) return 1f;
		inCycle -= LitSeconds;

		if (inCycle < FallSeconds)
		{
			return Ease(1f - inCycle / FallSeconds);
		}

		return 0f; // Resting until the cycle wraps.
	}

	/// <summary>Smoothstep over an assumed 0..1 input: eases into and out of a ramp.</summary>
	private static float Ease(float t)
	{
		float clamped = Mathf.Clamp(t, 0f, 1f);
		return clamped * clamped * (3f - 2f * clamped);
	}

	/// <summary>True when a world position (at any height) is over this pool's footprint.</summary>
	public bool ContainsXZ(Vector3 worldPosition)
	{
		return ContainsXZ(new Vector2(worldPosition.X, worldPosition.Z));
	}

	/// <summary>
	/// True when a ground-plane position falls inside this pool's square
	/// footprint — the rounded shape is tested as its square, so the clipped
	/// corners still count as pool. Edges count as inside.
	/// </summary>
	public bool ContainsXZ(Vector2 worldXZ)
	{
		Vector2 offset = worldXZ - new Vector2(GlobalPosition.X, GlobalPosition.Z);
		float half = Side * 0.5f;
		return Mathf.Abs(offset.X) <= half && Mathf.Abs(offset.Y) <= half;
	}

	/// <summary>
	/// True when a circle of <paramref name="radius"/> centred on
	/// <paramref name="worldXZ"/> touches this pool.
	///
	/// A circle rather than a point because what will be tested is a node or an
	/// ammo round, which has a size of its own: the closest point of the square to
	/// the circle's centre is what decides it.
	/// </summary>
	/// <param name="worldXZ">Centre of the circle, on the ground plane.</param>
	/// <param name="radius">Radius of the circle, in world units.</param>
	public bool OverlapsXZ(Vector2 worldXZ, float radius)
	{
		Rect2 footprint = Footprint;
		var closest = new Vector2(
			Mathf.Clamp(worldXZ.X, footprint.Position.X, footprint.End.X),
			Mathf.Clamp(worldXZ.Y, footprint.Position.Y, footprint.End.Y));
		return closest.DistanceSquaredTo(worldXZ) <= radius * radius;
	}
}

using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;

namespace MbcPrototype.World;

/// <summary>
/// The map's purple pools: <see cref="PurplePool"/>s scattered over the ground,
/// one per-frame loop that pulses them all, and the "which pool is at this spot"
/// queries the collision checks will come to use.
///
/// Nothing is generated at random and nothing is loaded. Each pool's position
/// comes from a fixed integer hash of its index, so the same pools sit in the
/// same places on every run — and, unlike a grid or a low-discrepancy sequence,
/// they do not line up. Nothing in the game reacts to a pool yet: the pools are
/// decoration plus the queries in this class.
///
/// Built in code and parented inside the scene (see GameManager.SpawnContainer),
/// like every other thing a match spawns.
/// </summary>
public partial class PoolField : Node3D
{
	/// <summary>Side of one pool, in world units — three node diameters.</summary>
	public float PoolSide { get; private set; } = 6.0f;

	/// <summary>
	/// Share of the field's ground the pool footprints are meant to cover: 1/100
	/// by default, so the map is dotted with pools rather than blanket-covered.
	/// The number of pools follows from it (see <see cref="CoveredFraction"/>).
	/// </summary>
	public float CoverageFraction { get; private set; } = 0.01f;

	/// <summary>The ground rectangle the pools are scattered over.</summary>
	public Rect2 Field { get; private set; }

	/// <summary>
	/// Pool footprint as a fraction of <see cref="Field"/>: what "the pools cover
	/// about 1/100 of the ground" comes to once the scatter has run.
	///
	/// It counts the pools themselves and not the sandy rims around them: the rim
	/// is scenery outside the pool's footprint, so it neither adds to the cover nor
	/// is a place anything can land "in the pool".
	/// </summary>
	public float CoveredFraction { get; private set; }

	/// <summary>
	/// Every pool on the map, in placement order. The future collision checks read
	/// this; nothing else should mutate it.
	/// </summary>
	public IReadOnlyList<PurplePool> Pools => _pools;

	private readonly List<PurplePool> _pools = new List<PurplePool>();

	/// <summary>
	/// Builds the field: a <see cref="PoolField"/> dotted with pools, and returns
	/// it. Nothing has to be kept of the return value — the pools pulse themselves
	/// once placed — but <c>GameManager.Pools</c> keeps it for the queries below.
	/// </summary>
	/// <param name="caller">
	/// A node already in the tree, used only for the fallback parent before the
	/// match has been set up.
	/// </param>
	/// <param name="field">The ground rectangle to scatter over, in world XZ.</param>
	/// <param name="poolSide">Side of one pool, in world units.</param>
	/// <param name="coverageFraction">Share of the field the pool footprints should cover.</param>
	public static PoolField Spawn(Node caller, Rect2 field, float poolSide, float coverageFraction)
	{
		var poolField = new PoolField { Name = "PoolField" };

		// Parented inside the scene, never the tree root: a restart reloads the
		// scene alone, so a field under the tree root would outlive the match it
		// belongs to.
		Node parent = GameManager.Instance?.SpawnContainer;
		if (parent == null) parent = caller.GetTree().Root;
		parent.AddChild(poolField);

		poolField.Field = field;
		poolField.PoolSide = Mathf.Max(0.01f, poolSide);
		poolField.CoverageFraction = Mathf.Max(0f, coverageFraction);

		// One shape for the whole map: every pool is the same size, so the rounded
		// square and its sandy rim are built once here (there is no rounded-square
		// primitive to instantiate) and shared by every pool placed.
		poolField.Scatter(PurplePool.CreateMesh(poolField.PoolSide));
		return poolField;
	}

	/// <summary>
	/// Scatters the pools: how many the coverage target needs, placed at positions
	/// a fixed hash hands out, keeping only the candidates that have room.
	///
	/// Nothing here is random in the sense of a generator with a seed — a
	/// candidate position is a pure function of its index, so the map is identical
	/// on every run, and the layout needs neither a seed nor a stored table. The
	/// hash does not spread its points evenly, unlike a grid or a low-discrepancy
	/// sequence, which is the point: the pools read as scattered across the ground
	/// rather than as rows, with the spacing rule below keeping that from turning
	/// into two pools on top of one another.
	/// </summary>
	/// <param name="mesh">The shared pool shape, built by <see cref="PurplePool.CreateMesh"/>.</param>
	private void Scatter(ArrayMesh mesh)
	{
		float side = PoolSide;
		float poolArea = side * side;
		float fieldArea = Mathf.Max(0.01f, Field.Size.X * Field.Size.Y);
		int wanted = Mathf.Max(1, Mathf.RoundToInt(CoverageFraction * fieldArea / poolArea));

		// A pool keeps half a footprint — pool plus rim — clear of every edge, so
		// no pool and no sand hangs over the map.
		float margin = side * (0.5f + PurplePool.BorderWidthFraction);
		float minX = Field.Position.X + margin;
		float maxX = Field.End.X - margin;
		float minY = Field.Position.Y + margin;
		float maxY = Field.End.Y - margin;
		if (maxX < minX || maxY < minY)
		{
			GD.PushWarning($"[Pools] A {side}-unit pool does not fit a "
				+ $"{Field.Size.X}x{Field.Size.Y} field — no pools placed.");
			return;
		}

		// Two pools may not come closer than their footprints plus their rims, so
		// no two pools ever touch or overlap. The hash decides *where* a pool may
		// go; this rule only turns away the candidates that would collide, which
		// keeps the count honest and the map readable.
		float separation = side * (1f + 2f * PurplePool.BorderWidthFraction);

		int candidate = 0;
		int candidatesTried = 0;
		int candidateCap = wanted * 40; // Room to spare; rejected candidates are simply skipped.
		while (_pools.Count < wanted && candidatesTried < candidateCap)
		{
			Vector2 unit = CandidateAt(candidate);
			candidate++;
			candidatesTried++;

			var position = new Vector2(
				Mathf.Lerp(minX, maxX, unit.X),
				Mathf.Lerp(minY, maxY, unit.Y));
			if (HasNeighbourWithin(position, separation)) continue;

			_pools.Add(PurplePool.Spawn(this, position, side, PhaseFor(_pools.Count), mesh));
		}

		if (_pools.Count < wanted)
		{
			GD.PushWarning($"[Pools] Only {_pools.Count} of {wanted} pools fit with "
				+ $"{separation} units between them.");
		}

		CoveredFraction = _pools.Count * poolArea / fieldArea;
		GD.Print($"[Pools] {_pools.Count} pools of side {side} scattered over "
			+ $"{Field.Size.X}x{Field.Size.Y} of ground = {CoveredFraction:P1} covered "
			+ $"(target {CoverageFraction:P1}).");
	}

	/// <summary>
	/// The fixed candidate position for an index, as a point in the unit square.
	///
	/// <paramref name="index"/> walks on whenever a candidate is rejected, so the
	/// layout depends on the order candidates are tested in — and on nothing else.
	/// There is no state to save and no seed to keep: this function is the map.
	/// </summary>
	/// <param name="index">Which candidate to produce.</param>
	private static Vector2 CandidateAt(int index)
	{
		// Two different salts: the same index has to give an X and a Z that are not
		// related to each other, or every pool would sit on the diagonal.
		return new Vector2(Hash01(index, 1), Hash01(index, 2));
	}

	/// <summary>
	/// An integer hash of <paramref name="index"/> and <paramref name="salt"/>, as a
	/// value in 0..1.
	///
	/// The mix is MurmurHash3's finalizer: cheap, and it avalanches, so indices one
	/// apart land far apart in the unit square. It is a formula, not a generator —
	/// the same inputs give the same output forever, which is what makes the pool
	/// map the same map every time the game is run.
	/// </summary>
	/// <param name="index">Sequence position.</param>
	/// <param name="salt">Selects which of the coordinate's independent streams is hashed.</param>
	private static float Hash01(int index, int salt)
	{
		uint h = (uint)(index * 0x9E3779B1) ^ (uint)(salt * 0x85EBCA77);
		h ^= h >> 16;
		h *= 0x7FEB352D;
		h ^= h >> 15;
		h *= 0x846CA68B;
		h ^= h >> 16;
		return (h >> 8) * (1f / 16777216f); // Top 24 bits, scaled to 0..1.
	}

	/// <summary>
	/// True when an already-placed pool is closer than <paramref name="separation"/>
	/// to the candidate position. The field sits at the origin, so a pool's global
	/// position is its world position.
	/// </summary>
	private bool HasNeighbourWithin(Vector2 position, float separation)
	{
		float limit = separation * separation;
		foreach (PurplePool pool in _pools)
		{
			var placed = new Vector2(pool.GlobalPosition.X, pool.GlobalPosition.Z);
			if (placed.DistanceSquaredTo(position) < limit) return true;
		}
		return false;
	}

	/// <summary>
	/// The point in the pulse this pool starts at, from its place in the scatter.
	///
	/// A low-discrepancy walk rather than a random one: the value is fixed, so
	/// the map is identical on every run, but neighbours land far apart in the
	/// cycle — the field shimmers instead of pulsing as one sheet.
	/// </summary>
	/// <param name="index">The pool's position in placement order.</param>
	private static float PhaseFor(int index)
	{
		// The golden ratio's fractional part: successive multiples spread evenly
		// over 0..1 without ever repeating.
		const float GoldenRatioConjugate = 0.6180339887f;
		return Mathf.PosMod(index * GoldenRatioConjugate, 1f);
	}

	/// <summary>
	/// The first pool whose footprint covers a ground position, or null when that
	/// position is bare ground. The "what did this land in" query for something
	/// with no size of its own.
	/// </summary>
	/// <param name="worldPosition">Any world position; only X and Z are read.</param>
	public PurplePool PoolAt(Vector3 worldPosition)
	{
		return PoolAt(new Vector2(worldPosition.X, worldPosition.Z));
	}

	/// <summary>Ground-plane form of <see cref="PoolAt(Vector3)"/>.</summary>
	/// <param name="worldXZ">A position on the ground plane.</param>
	public PurplePool PoolAt(Vector2 worldXZ)
	{
		foreach (PurplePool pool in _pools)
		{
			if (pool.ContainsXZ(worldXZ)) return pool;
		}
		return null;
	}

	/// <summary>True when a ground position lies in any pool at all.</summary>
	/// <param name="worldPosition">Any world position; only X and Z are read.</param>
	public bool IsInAnyPool(Vector3 worldPosition)
	{
		return PoolAt(worldPosition) != null;
	}

	/// <summary>
	/// Fills <paramref name="results"/> with every pool a circle of
	/// <paramref name="radius"/> centred on <paramref name="worldXZ"/> touches —
	/// the query for what lands in a pool when what lands has a size (a node, an
	/// ammo round). The buffer is cleared first, so a caller can keep one list and
	/// reuse it: this is meant to run per landing, not per frame per pool.
	/// </summary>
	/// <param name="worldXZ">Centre of the circle, on the ground plane.</param>
	/// <param name="radius">Radius of the circle, in world units.</param>
	/// <param name="results">Buffer to fill; ignored when null.</param>
	public void OverlappingPools(Vector2 worldXZ, float radius, List<PurplePool> results)
	{
		if (results == null) return;

		results.Clear();
		foreach (PurplePool pool in _pools)
		{
			if (pool.OverlapsXZ(worldXZ, radius)) results.Add(pool);
		}
	}

	/// <summary>
	/// The single loop that drives every pool's pulse.
	///
	/// Pools do not run their own _Process: one pass here is cheaper than hundreds
	/// of separate callbacks, and it keeps the field's timing in one place instead
	/// of spread across the pools.
	/// </summary>
	/// <param name="delta">Seconds since the last frame.</param>
	public override void _Process(double delta)
	{
		float step = (float)delta;
		foreach (PurplePool pool in _pools)
		{
			if (IsInstanceValid(pool)) pool.Advance(step);
		}
	}
}

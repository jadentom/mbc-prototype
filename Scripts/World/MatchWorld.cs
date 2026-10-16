using Godot;

namespace MbcPrototype.World;

/// <summary>
/// The world a match is fought in: the map it loads, the bounds that map
/// describes, and the energy pools scattered across it (docs/parity M0.4, M0.6).
///
/// It is the one thing that knows where the map is. The minimap asks it for a
/// rectangle, a shot asks it what it landed in, the match asks it where a seat
/// starts — and nothing else has to hold a <see cref="MapDefinition"/> to answer
/// those questions. M3 gives it a height field to own as well; today it owns the
/// pools and the bounds.
/// </summary>
public partial class MatchWorld : Node3D
{
	/// <summary>
	/// Extent of the world when the match has no map resource at all. A fallback
	/// for running with no content (§2), never a map: the match says so at startup
	/// through <see cref="Build"/>, and this only keeps the minimap and the pools
	/// from dividing by zero.
	/// </summary>
	public const float FallbackExtent = 100f;

	/// <summary>The map this match is fought on; null when the match has none.</summary>
	public MapDefinition Map { get; private set; }

	/// <summary>
	/// The map's purple pools, or null when the map asks for none.
	///
	/// Nothing reads a pool yet: the pools are decoration. This is the hook the
	/// future collision checks use — <c>Pools.PoolAt(position)</c> for a point and
	/// <c>Pools.OverlappingPools(centre, radius, buffer)</c> for something with a
	/// size.
	/// </summary>
	public PoolField Pools { get; private set; }

	/// <summary>
	/// The play area as a rectangle in world XZ: the map's bounds, or a fallback
	/// square centred on the origin when there is no map. What the minimap shows
	/// and what the pools are scattered across.
	/// </summary>
	public Rect2 Bounds => Map != null ? Map.Bounds : CenteredSquare(FallbackExtent);

	/// <summary>Side of the largest square the map fits in: the minimap's own size.</summary>
	public float Extent => Map != null ? Mathf.Max(Map.Extent, 1f) : FallbackExtent;

	/// <summary>
	/// Loads the map, validates it and lays its pools over its own ground.
	///
	/// The pools and the minimap both read this object afterwards, so the pools,
	/// the minimap and the start positions cannot disagree about where the map is.
	/// </summary>
	/// <param name="map">The map resource, or null for a match with no content.</param>
	/// <param name="mainCamera">
	/// The camera that draws the world, told to include the pools' render layer
	/// (the minimap camera excludes it and draws flat markers instead).
	/// </param>
	public void Build(MapDefinition map, Camera3D mainCamera)
	{
		Map = map;

		if (Map == null)
		{
			GD.PushError($"MatchWorld: no MapDefinition assigned — the match has no bounds and both sides start at the origin. "
				+ $"The world falls back to a {FallbackExtent}-unit square.");
		}
		else
		{
			Map.Validate();
			GD.Print($"[Match] Map: {Map.Describe()}.");
		}

		Pools = Map?.CreatePoolField(this);
		if (Pools == null) return;

		// Pools are drawn on a layer of their own so the minimap camera can skip
		// them and draw flat, steady-coloured markers instead. The main camera has
		// to be told to draw that layer.
		if (mainCamera != null) mainCamera.CullMask |= PurplePool.WorldRenderLayer;
	}

	/// <summary>
	/// Where a seat of this match begins. The map owns these (docs/parity M0.4);
	/// without one every seat falls back to the origin, which is visibly broken
	/// and says so through <see cref="Build"/> rather than silently inventing a
	/// layout.
	/// </summary>
	public Vector3 SeatPosition(int seat)
	{
		return Map != null ? Map.StartPosition(seat) : Vector3.Zero;
	}

	/// <summary>How many seats this map can start.</summary>
	public int SeatCount => Map?.SeatCount ?? 0;

	private static Rect2 CenteredSquare(float extent)
	{
		return new Rect2(Vector2.One * (-extent * 0.5f), Vector2.One * extent);
	}
}

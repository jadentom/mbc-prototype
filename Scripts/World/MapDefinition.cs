using Godot;

namespace MbcPrototype.World;

/// <summary>
/// A map: the ground a match is fought on, as a resource a designer can edit
/// (docs/parity §2, §7 — "world" is the map's numbers, and this is where they
/// live).
///
/// It holds four things:
///
/// - **bounds** — the play area on the ground plane. This is what the minimap
///   draws and what the pools are scattered across; before M0.4 both of those
///   guessed their extent from the minimap widget and the ground was a
///   10000×10000 plane with no bounds at all;
/// - **the height field and its terrain states** — see
///   <see cref="TerrainHeights"/> and <see cref="Terrain"/>; data today, played
///   in M3;
/// - **the energy pools** — how big they are and how much of the map they cover;
/// - **the start positions** — where a 1v1 match begins, one per seat, instead
///   of "wherever the scene's structure happens to be" and "two screen widths to
///   the right of it".
///
/// M0.4 ships exactly one of these — `Resources/Maps/development_map.tres` —
/// and it is the map every milestone is developed and tuned on until M9's editor
/// authors more. The C# field defaults below are what a *new* map starts with in
/// the editor; the resource is the source of truth (§2).
/// </summary>
[GlobalClass]
public partial class MapDefinition : Resource
{
	/// <summary>Stable identifier, for code and saved matches. Not shown to the player.</summary>
	[Export] public string Id = "";

	/// <summary>Name shown in a setup screen (docs/parity M6).</summary>
	[Export] public string DisplayName = "";

	// ------------------------------------------------------------------
	// Bounds
	// ------------------------------------------------------------------

	/// <summary>Middle of the play area, in world units on the ground plane (XZ).</summary>
	[Export] public Vector2 Center = Vector2.Zero;

	/// <summary>
	/// Side lengths of the play area, in world units (X across, Y along Z).
	/// Square today. The ground mesh is still a 10000×10000 plane underneath
	/// this: the bounds say where the map *is*, and M3's wrapping world is what
	/// makes leaving them mean something.
	///
	/// Zero means "not set", and <see cref="Validate"/> reports it: a map has no
	/// size until its resource says so. Godot does not write a value that equals
	/// the script's default into the resource, so a default that *was* a real map
	/// size would be the same number living in two places (§2).
	/// </summary>
	[Export] public Vector2 Size = Vector2.Zero;

	/// <summary>The play area as a rectangle in world XZ, ready for a minimap or a scatter.</summary>
	public Rect2 Bounds => new Rect2(Center - (Size * 0.5f), Size);

	/// <summary>
	/// The map's extent as one number: what a square minimap shows and what a
	/// scatter measures its coverage against. The larger axis, because the
	/// minimap widget is square and a map that does not fit would be cropped.
	/// </summary>
	public float Extent => Mathf.Max(Size.X, Size.Y);

	// ------------------------------------------------------------------
	// Terrain (data only until M3)
	// ------------------------------------------------------------------

	/// <summary>
	/// How many cells across the height field is painted. Zero (with
	/// <see cref="Terrain"/> empty) means the map is flat ground everywhere.
	///
	/// The cell size follows from the bounds: a map is `GridWidth` cells wide
	/// across <see cref="Size"/>.X. A finer grid is a more detailed map, not a
	/// bigger one.
	/// </summary>
	[Export] public int GridWidth = 0;

	/// <summary>How many cells down the height field is painted. See <see cref="GridWidth"/>.</summary>
	[Export] public int GridHeight = 0;

	/// <summary>
	/// The height field: one <see cref="TerrainState"/> per cell, row-major
	/// (first <see cref="GridWidth"/> entries are the top row), or empty for a
	/// flat map of ground.
	///
	/// Stored as states rather than as heights so that changing what a plateau
	/// is worth is one edit in <see cref="TerrainHeights"/> — the same numbers
	/// everywhere a plateau appears, rather than one number per cell (§2: one
	/// home per number).
	/// </summary>
	[Export] public int[] Terrain = System.Array.Empty<int>();

	/// <summary>
	/// Height of each terrain state, in world units, indexed by
	/// <see cref="TerrainState"/> — so entry 0 is ground level, entry 1 a
	/// plateau, entry 2 a mountain. Empty, and any state without an entry, means
	/// height zero.
	///
	/// A **guess** (docs/parity §7): nobody published the original's terrain
	/// heights. Nothing reads these yet — M3 turns them into the world, the
	/// height→range curve and the flat-ground landing rule.
	///
	/// A `PackedFloat32Array` in Godot's terms; C# binds those to a plain array.
	/// </summary>
	[Export] public float[] TerrainHeights = System.Array.Empty<float>();

	/// <summary>Height of a terrain state, or zero when the map does not say.</summary>
	public float HeightOf(TerrainState state)
	{
		int index = (int)state;
		return TerrainHeights != null && index >= 0 && index < TerrainHeights.Length ? TerrainHeights[index] : 0f;
	}

	/// <summary>
	/// True when nothing on this map stands above ground level: either no height
	/// field is painted at all, or every cell in it is
	/// <see cref="TerrainState.Ground"/>. A map can carry terrain heights while
	/// being flat — those are what a plateau would be worth *if* one were painted.
	/// </summary>
	public bool IsFlat
	{
		get
		{
			if (Terrain == null) return true;

			foreach (int state in Terrain)
			{
				if ((TerrainState)state != TerrainState.Ground) return false;
			}
			return true;
		}
	}

	// ------------------------------------------------------------------
	// Energy pools
	// ------------------------------------------------------------------

	/// <summary>
	/// Side of one energy pool, in world units; the development map uses three
	/// structure diameters (a structure's radius is 1.0 — see
	/// Scenes/structure.tscn). Zero, or a zero <see cref="PoolCoverageFraction"/>,
	/// means this map has no pools.
	/// </summary>
	[Export] public float PoolSide = 0f;

	/// <summary>
	/// Share of the map's ground the pools cover. The count of pools follows from
	/// it, so this is the number to change to make a map richer or sparser in
	/// energy; the development map uses 1/100. Zero means this map has no pools.
	/// </summary>
	[Export(PropertyHint.Range, "0,0.5")] public float PoolCoverageFraction = 0f;

	/// <summary>
	/// Scatters this map's pools over its own bounds. Returns null when the map
	/// asks for no pools.
	///
	/// The layout comes from the field's fixed hash of a candidate index, not
	/// from a seed (see <see cref="PoolField.Scatter"/>) — and now from *this*
	/// map's bounds rather than from whatever region the minimap was showing.
	/// Authored, hand-placed pools of varying size are M9's editor; the map
	/// already owns the numbers that decide the field.
	/// </summary>
	/// <param name="caller">Node the field is parented under (in the scene, never the tree root).</param>
	public PoolField CreatePoolField(Node caller)
	{
		if (PoolSide <= 0f || PoolCoverageFraction <= 0f) return null;
		return PoolField.Spawn(caller, Bounds, PoolSide, PoolCoverageFraction);
	}

	// ------------------------------------------------------------------
	// Start positions
	// ------------------------------------------------------------------

	/// <summary>
	/// Where each seat of a match begins, in world XZ. Two entries for a 1v1:
	/// seat 0 is the human's, seat 1 the AI's (docs/parity M6's setup screen is
	/// what will let a player choose otherwise).
	///
	/// These replace "the structure in the scene" and "two screen widths to the
	/// right of it" — which made the distance between the two sides depend on
	/// the window's aspect ratio.
	/// </summary>
	[Export] public Vector2[] StartPositions = System.Array.Empty<Vector2>();

	/// <summary>How many seats this map can start.</summary>
	public int SeatCount => StartPositions?.Length ?? 0;

	/// <summary>
	 /// The starting position of <paramref name="seat"/>, on the ground plane
	 /// (y = 0), or the map's centre for a seat the map does not define.
	 /// </summary>
	public Vector3 StartPosition(int seat)
	{
		if (seat < 0 || seat >= SeatCount) return new Vector3(Center.X, 0f, Center.Y);

		Vector2 start = StartPositions[seat];
		return new Vector3(start.X, 0f, start.Y);
	}

	/// <summary>
	/// Reports the map's own data mistakes — a map too small to play on, a
	/// height field that does not match its grid, a start position off the map,
	/// a 1v1 without two seats — so a content error surfaces at match start
	/// rather than as a missing structure later.
	/// </summary>
	public void Validate()
	{
		if (Size.X <= 0f || Size.Y <= 0f)
		{
			GD.PushError($"MapDefinition '{Id}': size {Size} has no area to play on.");
		}

		if (SeatCount < 2)
		{
			GD.PushError($"MapDefinition '{Id}': {SeatCount} start position(s) — a 1v1 needs two.");
		}

		Rect2 bounds = Bounds;
		for (int seat = 0; seat < SeatCount; seat++)
		{
			if (!bounds.HasPoint(StartPositions[seat]))
			{
				GD.PushError($"MapDefinition '{Id}': start position {seat} {StartPositions[seat]} is outside the map's bounds {bounds}.");
			}
		}

		int painted = Terrain?.Length ?? 0;
		int cells = GridWidth * GridHeight;
		if (cells > 0 && painted != cells)
		{
			GD.PushError($"MapDefinition '{Id}': the height field has {painted} cells but the grid is {GridWidth}x{GridHeight} = {cells}.");
		}
		else if (cells == 0 && painted > 0)
		{
			GD.PushError($"MapDefinition '{Id}': the height field has {painted} cells but the grid is unpainted (0x0).");
		}

		foreach (int state in Terrain ?? System.Array.Empty<int>())
		{
			if (!System.Enum.IsDefined(typeof(TerrainState), state))
			{
				GD.PushError($"MapDefinition '{Id}': the height field uses terrain state {state}, which is not one of the three.");
			}
		}

		int states = System.Enum.GetValues(typeof(TerrainState)).Length;
		int heights = TerrainHeights?.Length ?? 0;
		if (heights > 0 && heights != states)
		{
			GD.PushError($"MapDefinition '{Id}': {heights} terrain heights for {states} terrain states.");
		}
	}

	/// <summary>One line describing this map, for the match-start log.</summary>
	public string Describe()
	{
		string terrain = IsFlat ? "flat" : $"{GridWidth}x{GridHeight} height field";
		return $"{Id} ({Size} bounds, {terrain}, pool side {PoolSide} over {PoolCoverageFraction:P0}, {SeatCount} start position(s))";
	}
}

namespace MbcPrototype.World;

/// <summary>
/// What the ground of a map cell is.
///
/// **Three** states, not the original's four: the first slice has no water
/// (docs/parity §1), and with it go Bridges, structures destroyed by landing in
/// water and the Crawler's water travel. Terrain is a height-and-cover puzzle
/// here, not a river crossing.
///
/// The height that goes with each state is the map's data, not this enum's
/// (<see cref="MapDefinition.TerrainHeights"/>) — the enum only names them.
/// Nothing reads a cell's state yet: heights become gameplay in M3, which is
/// also where a structure landing on a hillside is destroyed on impact.
/// </summary>
public enum TerrainState
{
	/// <summary>Flat ground: the only state the development map uses today.</summary>
	Ground = 0,

	/// <summary>Raised ground: shoots from it go farther, shoots uphill fall short (M3).</summary>
	Plateau = 1,

	/// <summary>The high ground. Same rule as a plateau, further up (M3).</summary>
	Mountain = 2,
}

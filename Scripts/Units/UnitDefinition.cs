using Godot;

namespace MbcPrototype.Units;

/// <summary>
/// One entry in the roster: every number and reference that describes a unit,
/// as a <see cref="Resource"/> a designer can open and edit without touching
/// code (docs/parity §2).
///
/// This is the replacement for the old <c>AmmoType</c> enum. Nothing in the
/// game branches on "which ammo": the launcher takes one of these, instantiates
/// <see cref="ProjectileScene"/>, and hands the definition to whatever
/// behaviour that scene carries. Twelve units are therefore twelve resources,
/// not twelve branches.
///
/// Every value here is a first guess to iterate from — the manual's published
/// numbers where it has them, ours where it does not (docs/parity §7). None of
/// them is a target, and none of them belongs in a <c>.cs</c> file.
/// </summary>
[GlobalClass]
public partial class UnitDefinition : Resource
{
	/// <summary>
	/// Stable identifier, used by code and by saved matches to refer to this
	/// unit (catalog lookup, command log). Not shown to the player.
	/// </summary>
	[Export] public string Id = "";

	/// <summary>Name shown in the HUD and in the turn-event log.</summary>
	[Export] public string DisplayName = "";

	/// <summary>What this unit is, for code that does not know it by name.</summary>
	[Export] public UnitTraits Traits = UnitTraits.None;

	/// <summary>
	/// Build level, which is also its energy cost: 1, 3 or 7 (docs/parity §7).
	/// The build menu's pages are the levels.
	/// </summary>
	[Export] public int Level = 1;

	/// <summary>
	/// Hit points of the structure this unit deploys, subtracted from integer
	/// to integer by <see cref="MbcPrototype.Core.BaseNode.TakeDamage"/> — the
	/// manual's <em>Armor</em> column, which is hit points and not a damage
	/// threshold. Zero means the unit is not a structure (a Bomb has no hit
	/// points, because nothing can shoot at it).
	/// </summary>
	[Export] public int HitPoints = 0;

	/// <summary>
	/// Damage this unit deals where it goes off. A flat integer subtracted from
	/// the target's hit points: a Bomb (3) on a Hub (5) leaves 2, and partial
	/// damage is normal. Zero for a unit that does not damage anything.
	/// </summary>
	[Export] public int Damage = 0;

	/// <summary>
	/// Inner blast radius, in world units, measured along the ground from where
	/// the shot went off: inside it the target takes <see cref="Damage"/> in
	/// full. What happens between here and <see cref="OuterRadius"/> is the
	/// match's falloff curve (docs/parity §7), not this unit's business.
	/// </summary>
	[Export] public float DirectHitRadius = 0f;

	/// <summary>
	/// Outer blast radius, in world units: past it the shot takes nothing from
	/// anything. Zero means this unit has no blast at all.
	/// </summary>
	[Export] public float OuterRadius = 0f;

	/// <summary>
	/// How far this unit sees, in world units, when it is standing on the map.
	/// Unused until the vision system lands (docs/parity M4/M5); zero means it
	/// sees only what everyone sees.
	/// </summary>
	[Export] public float VisionRadius = 0f;

	/// <summary>
	/// Whether a player may build this unit at all. Units that exist in the
	/// roster but are not offered (a future scenario-only unit) set this false,
	/// and the HUD and the AI's build list both follow it.
	/// </summary>
	[Export] public bool CanBeBuilt = true;

	/// <summary>Icon the HUD draws for this unit. Art is ours and replaceable.</summary>
	[Export] public Texture2D Icon;

	/// <summary>
	/// The scene launched when this unit is fired — the behaviour behind the
	/// definition. Its root implements
	/// <see cref="MbcPrototype.Combat.ILaunchedAmmo"/>, which is how the
	/// launcher configures it without knowing what it is.
	/// </summary>
	[Export] public PackedScene ProjectileScene;

	/// <summary>True when this unit's shot deploys something with hit points.</summary>
	public bool IsStructure => Traits.HasFlag(UnitTraits.Structure);

	/// <summary>True when this unit damages what it strikes and what is near it.</summary>
	public bool IsWeapon => Traits.HasFlag(UnitTraits.Weapon);

	/// <summary>True when this unit goes off over an area rather than at a point.</summary>
	public bool HasBlast => OuterRadius > 0f;

	public override string ToString()
	{
		return string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;
	}
}

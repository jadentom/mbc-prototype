using System;

namespace MbcPrototype.Units;

/// <summary>
/// What a unit *is*, as far as code that does not know the unit by name is
/// concerned: the HUD, the launcher and the AI read these flags off a
/// <see cref="UnitDefinition"/> instead of testing for a concrete type.
///
/// Deliberately small. A flag earns its place here when something generic
/// actually branches on it; the behaviours themselves live on the ammo scene
/// behind <see cref="MbcPrototype.Combat.ILaunchedAmmo"/>, so a new unit is a
/// definition plus a behaviour and not a new enum value.
/// </summary>
[Flags]
public enum UnitTraits
{
	/// <summary>No traits at all: a plain projectile with no side effects.</summary>
	None = 0,

	/// <summary>
	/// Its shot deploys a structure: something with hit points that joins the
	/// cord network and is owned by the combatant that fired it.
	/// </summary>
	Structure = 1,

	/// <summary>
	/// It damages what it strikes and what is near where it goes off, by
	/// distance along the ground (<see cref="UnitDefinition.Damage"/> and its
	/// blast radii).
	/// </summary>
	Weapon = 2,

	/// <summary>
	/// It may launch units: a structure with this flag can be the origin of a
	/// build, and one without it cannot, however it got onto the map. Today the
	/// only launcher is the Hub (docs/parity M1), and the flag is what keeps
	/// that a property of the unit rather than a test for the id "hub" inside
	/// the launcher. A second launcher is therefore data plus reach, not a
	/// branch (M4's *launcher capability*).
	/// </summary>
	Launcher = 4,

	/// <summary>
	/// The chain's **main Hub**: the only structure that generates the
	/// per-round income (docs/parity M1, [units.md](../../docs/parity/units.md)).
	/// Only a *root* structure with this flag pays it, so a Hub built outward
	/// from another Hub builds but does not generate. The amount is the match's
	/// <see cref="MbcPrototype.Rules.MatchRules.MainHubIncome"/>, not the unit's
	/// business.
	/// </summary>
	Hub = 8,

	/// <summary>
	/// It generates energy at the end of every round, more of it while it stands
	/// on an energy pool: the original's Energy Collector. The amounts are the
	/// match's (<see cref="MbcPrototype.Rules.MatchRules.HarvesterIncomeOnPool"/>
	/// and its off-pool sibling), because they are balance; the flag says what
	/// shape of income this structure is.
	/// </summary>
	Harvester = 16,
}

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
}

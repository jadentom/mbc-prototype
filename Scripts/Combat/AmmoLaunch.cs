using Godot;
using MbcPrototype.Core;
using MbcPrototype.Rules;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Combat;

/// <summary>
/// Everything a piece of ammo is handed at the moment it is launched. Passed
/// once, before the ammo enters the tree, by
/// <see cref="AmmoLauncher.Launch"/> — the launcher never reaches into an ammo
/// scene to set fields, so adding a unit means writing a behaviour and not
/// touching the launch path.
/// </summary>
public readonly struct AmmoLaunch
{
	public AmmoLaunch(UnitDefinition unit, MatchRules rules, BaseNode origin, Vector3 velocity, TurnEvent turnEvent)
	{
		Unit = unit;
		Rules = rules;
		Origin = origin;
		Velocity = velocity;
		TurnEvent = turnEvent;
	}

	/// <summary>The roster entry being fired: damage, blast radii, and the name in the log.</summary>
	public UnitDefinition Unit { get; }

	/// <summary>The match's global rules (the falloff curve, and whatever else §7 puts here).</summary>
	public MatchRules Rules { get; }

	/// <summary>The structure the shot was launched from.</summary>
	public BaseNode Origin { get; }

	/// <summary>
	/// Initial velocity, in world units per second: aim direction times the
	/// launch force the charge level asked for, plus the upward bias.
	/// </summary>
	public Vector3 Velocity { get; }

	/// <summary>
	/// The turn event this shot must resolve before control returns to the next
	/// combatant. The ammo marks it resolved when its work is done.
	/// </summary>
	public TurnEvent TurnEvent { get; }
}

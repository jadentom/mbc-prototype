using Godot;
using MbcPrototype.Core;
using MbcPrototype.Rules;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Combat;

/// <summary>
/// The single place ammo is put into the world. Both the player and every AI
/// combatant launch through here, so aiming conventions, power mapping and
/// turn-event registration can never drift between them.
///
/// The launcher knows nothing about *which* unit it is firing: it takes a
/// <see cref="UnitDefinition"/> from the roster (docs/parity §7), instantiates
/// the scene that definition names, and hands the shot to that scene's
/// behaviour through <see cref="ILaunchedAmmo"/>.
/// </summary>
public static class AmmoLauncher
{
	/// <summary>
	/// The ground direction an aim angle points along. The aim indicator is a
	/// box rotated around Y, so its forward (+Z) axis after a rotation of
	/// <paramref name="aimAngleRadians"/> is <c>(sin, 0, cos)</c> — the inverse
	/// of this mapping (atan2(x, z)) is how a direction becomes an aim angle.
	/// </summary>
	public static Vector3 DirectionFromAimAngle(float aimAngleRadians)
	{
		return new Vector3(Mathf.Sin(aimAngleRadians), 0f, Mathf.Cos(aimAngleRadians));
	}

	/// <summary>
	/// Instantiates the given unit's ammo from <paramref name="origin"/>,
	/// launches it with the match's ballistics and the launcher's own reach, and
	/// registers the TurnEvent that keeps the turn alive until the shot has fully
	/// resolved.
	/// </summary>
	/// <param name="unit">Which unit to fire, from the match's catalog.</param>
	/// <param name="origin">The structure the shot is launched from.</param>
	/// <param name="aimAngleRadians">Aim angle, in the aim-indicator convention.</param>
	/// <param name="powerPercent">Charge level, 0..1, mapped onto the min/max launch force.</param>
	/// <param name="description">Turn-event description, for debug logs (null uses the unit's name).</param>
	/// <returns>
	/// True when the shot was fired and its turn event registered, false when
	/// nothing left the launcher (no match, no definition, no scene, no
	/// container). The caller charges for the build only on true, so a build that
	/// could not happen costs nothing (docs/parity M1).
	/// </returns>
	public static bool Launch(UnitDefinition unit, Structure origin, float aimAngleRadians, float powerPercent, string description)
	{
		GameManager gm = GameManager.Instance;
		if (gm == null) return false;

		// A missing definition is a content error, not a silent no-op: nothing
		// was launched, so the caller's turn has no event to wait for.
		if (unit == null)
		{
			GD.PushError("AmmoLauncher: no unit definition to launch.");
			return false;
		}
		if (unit.ProjectileScene == null)
		{
			GD.PushError($"AmmoLauncher: unit '{unit.Id}' has no projectile scene to launch.");
			return false;
		}
		if (origin == null || !GodotObject.IsInstanceValid(origin) || origin.IsDestroyed) return false;

		// Every number the shot is made of comes from the match's rules and from
		// the launcher's own definition (docs/parity §7): the force range, the
		// upward bias and the muzzle height are the rules'; the reach multiplier is
		// the *launcher's*, which is what makes one launcher out-throw another
		// without a line of code. Nothing here is a literal a designer cannot reach.
		MatchRules rules = gm.ActiveRules;
		float reachScale = origin.Definition?.LaunchForceScale ?? 0f;
		float force = rules.LaunchForce(powerPercent, reachScale);
		Vector3 velocity = (DirectionFromAimAngle(aimAngleRadians) * force) + (Vector3.Up * rules.EffectiveUpwardBias);

		// The projectile (and everything it triggers — landing, damage,
		// destruction cascades, etc.) must fully resolve before control
		// returns to the next combatant.
		TurnEvent turnEvent = new TurnEvent(description ?? unit.DisplayName);

		var instance = unit.ProjectileScene.Instantiate<Node3D>();
		// Set before AddChild: a node that is not in the tree yet has no global
		// transform to read, and the spawn container sits at the origin, so the
		// local transform is the global one. The height it starts at is the rules'
		// muzzle height, not a literal: it is the number that moves when arcs do.
		instance.Position = origin.GlobalPosition + Vector3.Up * rules.EffectiveMuzzleHeight;

		if (instance is not ILaunchedAmmo ammo)
		{
			GD.PushError($"AmmoLauncher: {unit.ProjectileScene.ResourcePath} ('{unit.Id}') has no ILaunchedAmmo behaviour on its root — the shot cannot be configured.");
			instance.QueueFree();
			return false;
		}

		ammo.Initialize(new AmmoLaunch(unit, rules, origin, velocity, turnEvent));

		// Parented inside the scene, never the tree root: a reload of the scene
		// (how a finished match is restarted) frees the scene alone, so ammo
		// still in flight when the match ends would otherwise survive the restart.
		Node parent = gm.SpawnContainer;
		if (parent == null)
		{
			GD.PushError("AmmoLauncher: no container to parent the ammo under.");
			instance.QueueFree();
			return false;
		}
		parent.AddChild(instance);

		// A node that could not enter the tree (Godot refuses AddChild while a
		// parent is setting up children, and says so in the log) would leave a
		// TurnEvent with nothing behind it — a turn that can never resolve. Report
		// it and let the turn end instead.
		if (instance.GetParent() != parent)
		{
			GD.PushError($"AmmoLauncher: '{unit.Id}' could not be added to {parent.Name} — the shot was not fired.");
			instance.QueueFree();
			return false;
		}

		// Committing the turn: locks control until every event resolves.
		gm.RegisterTurnEvent(turnEvent);
		return true;
	}
}

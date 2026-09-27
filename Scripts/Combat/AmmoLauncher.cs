using Godot;
using MbcPrototype.Core;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Combat;

/// <summary>
/// The single place ammo is put into the world. Both the player and every AI
/// combatant launch through here, so aiming conventions, power mapping and
/// turn-event registration can never drift between them.
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
	/// Instantiates the given ammo from <paramref name="origin"/>, launches it
	/// with the combatant's shared force settings, and registers the TurnEvent
	/// that keeps the turn alive until the shot has fully resolved.
	/// </summary>
	/// <param name="ammo">Which ammo to launch.</param>
	/// <param name="origin">The node the shot is launched from.</param>
	/// <param name="aimAngleRadians">Aim angle, in the aim-indicator convention.</param>
	/// <param name="powerPercent">Charge level, 0..1, mapped onto the min/max launch force.</param>
	/// <param name="description">Turn-event description, for debug logs.</param>
	public static void Launch(AmmoType ammo, BaseNode origin, float aimAngleRadians, float powerPercent, string description)
	{
		GameManager gm = GameManager.Instance;
		if (gm == null) return;
		if (origin == null || !GodotObject.IsInstanceValid(origin) || origin.IsDestroyed) return;

		PackedScene scene = (ammo == AmmoType.Node) ? gm.ProjectileScene : gm.BombScene;
		if (scene == null)
		{
			GD.PushError($"AmmoLauncher: no scene assigned for {ammo} ammo.");
			return;
		}

		float force = Mathf.Lerp(gm.MinLaunchForce, gm.MaxLaunchForce, Mathf.Clamp(powerPercent, 0f, 1f));
		Vector3 velocity = (DirectionFromAimAngle(aimAngleRadians) * force) + (Vector3.Up * gm.UpwardBias);

		// The projectile (and everything it triggers — landing, damage,
		// destruction cascades, etc.) must fully resolve before control
		// returns to the next combatant.
		TurnEvent turnEvent = new TurnEvent(description ?? (ammo == AmmoType.Node ? "Node projectile" : "Bomb"));

		var instance = scene.Instantiate<Node3D>();
		// Set before AddChild: a node that is not in the tree yet has no global
		// transform to read, and the spawn container sits at the origin, so the
		// local transform is the global one.
		instance.Position = origin.GlobalPosition + Vector3.Up * 2.0f;

		// TODO: Switch this to inheritance and use an interface
		if (instance is Projectile projectile)
		{
			projectile.Velocity = velocity;
			projectile.CreatorNode = origin;
			projectile.TurnEvent = turnEvent;
		}
		else if (instance is Bomb bomb)
		{
			bomb.Velocity = velocity;
			bomb.TurnEvent = turnEvent;
		}
		else
		{
			GD.PushError($"AmmoLauncher: {scene.ResourcePath} is not a supported ammo scene.");
			instance.QueueFree();
			return;
		}

		// Parented inside the scene, never the tree root: a reload of the scene
		// (how a finished match is restarted) frees the scene alone, so ammo
		// still in flight when the match ends would otherwise survive the restart.
		Node parent = gm.SpawnContainer;
		if (parent == null)
		{
			GD.PushError("AmmoLauncher: no container to parent the ammo under.");
			instance.QueueFree();
			return;
		}
		parent.AddChild(instance);

		// Committing the turn: locks control until every event resolves.
		gm.RegisterTurnEvent(turnEvent);
	}
}

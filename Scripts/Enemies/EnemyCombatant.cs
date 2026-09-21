using Godot;
using MbcPrototype.Combat;
using MbcPrototype.Core;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Enemies;

/// <summary>
/// A node-chain combatant driven by a simple AI. It is the player's mirror
/// image: the same nodes, the same ammo, the same launch path and the same
/// defeat rule — the only difference is that its aim and power come from
/// <see cref="ChooseAimAngle"/> and a random roll instead of from input.
///
/// Each of its turns it plans exactly one action and releases it after
/// <see cref="ThinkTime"/>, alternating a node projectile and a bomb. Override
/// <see cref="ChooseAimAngle"/> (or the planning step in <see cref="BeginTurn"/>)
/// to build a smarter enemy without touching the turn system.
/// </summary>
public partial class EnemyCombatant : NodeChainCombatant
{
	/// <summary>
	/// Seconds spent aiming (and visibly charging) before the shot is released.
	/// Also the value the power preview ramps over.
	/// </summary>
	[Export] public float ThinkTime = 1.0f;

	/// <summary>
	/// Half-angle of the random aim cone around the direction to the target, in
	/// degrees. 0 aims dead straight, 180 fires in any direction at all.
	/// </summary>
	[Export] public float AimSpreadDegrees = 25.0f;

	/// <summary>Lowest launch power the AI will pick, as a fraction of the force range.</summary>
	[Export(PropertyHint.Range, "0,1")] public float MinPowerPercent = 0.3f;

	/// <summary>Highest launch power the AI will pick, as a fraction of the force range.</summary>
	[Export(PropertyHint.Range, "0,1")] public float MaxPowerPercent = 0.9f;

	public override bool IsPlayerControlled => false;

	/// <summary>
	/// The enemy always acts from the node closest to its target, so its chain
	/// creeps toward the player instead of piling up around its own root.
	/// </summary>
	public override BaseNode ActionNode
	{
		get
		{
			BaseNode target = PickTarget(RootPosition);
			return FindNodeClosestTo(target) ?? base.ActionNode;
		}
	}

	// The action planned at the start of the turn, released when the timer runs out.
	private bool _acting;
	private float _thinkTimer;
	private AmmoType _plannedAmmo;
	private float _plannedAimAngle;
	private float _plannedPowerPercent;

	/// <summary>Alternates node, bomb, node, … across this enemy's turns.</summary>
	private bool _launchNodeNext = true;

	public override void BeginTurn()
	{
		if (ActionNode == null)
		{
			// Nothing to act from: spend the turn instead of stalling the rotation.
			GameManager.Instance?.EndIdleTurn();
			return;
		}

		_acting = true;
		_thinkTimer = Mathf.Max(ThinkTime, 0f);

		// Alternate a chain-extending node with a damaging bomb, starting with a node.
		_plannedAmmo = _launchNodeNext ? AmmoType.Node : AmmoType.Bomb;
		_launchNodeNext = !_launchNodeNext;

		_plannedAimAngle = ChooseAimAngle();
		_plannedPowerPercent = (float)GD.RandRange(MinPowerPercent, MaxPowerPercent);

		Preview(0f);
		GD.Print($"[Enemy] Planning {_plannedAmmo}: {Mathf.RadToDeg(_plannedAimAngle):0}°, power {_plannedPowerPercent:0.00}");
	}

	public override void EndTurn()
	{
		_acting = false;
	}

	public override void _Process(double delta)
	{
		if (!_acting) return;

		BaseNode origin = ActionNode;
		if (origin == null)
		{
			// Lost the node it was aiming from: spend the turn rather than stall.
			_acting = false;
			GameManager.Instance?.EndIdleTurn();
			return;
		}

		_thinkTimer -= (float)delta;

		// Ramp the visible power up to the planned value while "thinking", so an
		// AI turn reads exactly like a player charge.
		float progress = ThinkTime > 0f ? Mathf.Clamp(1f - (_thinkTimer / ThinkTime), 0f, 1f) : 1f;
		Preview(_plannedPowerPercent * progress);

		if (_thinkTimer > 0f) return;

		_acting = false;
		Launch(_plannedAmmo, origin, _plannedAimAngle, _plannedPowerPercent);

		// If nothing could be launched (no ammo scene, dead origin) no TurnEvent
		// was registered and the rotation would wait forever — spend the turn.
		GameManager.Instance?.EndIdleTurn();
	}

	/// <summary>
	/// Picks a random aim direction: the direction to the target, opened up by a
	/// random angle within <see cref="AimSpreadDegrees"/>. With a small spread the
	/// AI stays dangerous; at 180° it is pure random.
	/// </summary>
	private float ChooseAimAngle()
	{
		BaseNode origin = ActionNode ?? RootNode;
		BaseNode target = PickTarget(RootPosition);
		if (origin == null || target == null)
		{
			return (float)GD.RandRange(-Mathf.Pi, Mathf.Pi);
		}

		Vector3 delta = target.GlobalPosition - origin.GlobalPosition;
		Vector3 flat = new Vector3(delta.X, 0f, delta.Z);
		if (flat.LengthSquared() < 0.0001f)
		{
			return (float)GD.RandRange(-Mathf.Pi, Mathf.Pi);
		}

		// The aim indicator points along +Z rotated by the aim angle, so the angle
		// facing `flat` is atan2(x, z) — the inverse of AmmoLauncher's mapping and
		// the same convention the player's aiming uses.
		float toTarget = Mathf.Atan2(flat.X, flat.Z);
		return toTarget + Mathf.DegToRad((float)GD.RandRange(-AimSpreadDegrees, AimSpreadDegrees));
	}

	/// <summary>The ground position used to measure "closest" when picking targets.</summary>
	private Vector3 RootPosition
	{
		get
		{
			return (RootNode != null && IsInstanceValid(RootNode) && !RootNode.IsDestroyed)
				? RootNode.GlobalPosition
				: Vector3.Zero;
		}
	}

	/// <summary>
	/// Nearest live node of a hostile combatant — anything on another team, which
	/// covers the player and, with more enemies on the field, their allies too.
	/// Returns null when there is nothing left to shoot at.
	/// </summary>
	private BaseNode PickTarget(Vector3 from)
	{
		GameManager gm = GameManager.Instance;
		if (gm == null) return null;

		BaseNode best = null;
		float bestDistance = float.MaxValue;

		foreach (Combatant combatant in gm.Combatants)
		{
			if (combatant == this || combatant.Team == Team) continue;
			if (combatant is not NodeChainCombatant chain) continue;

			foreach (BaseNode node in chain.Nodes)
			{
				if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

				float distance = from.DistanceSquaredTo(node.GlobalPosition);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = node;
				}
			}
		}

		return best;
	}

	/// <summary>This chain's live node nearest to <paramref name="target"/> (null when it has none).</summary>
	private BaseNode FindNodeClosestTo(BaseNode target)
	{
		if (target == null) return null;

		BaseNode best = null;
		float bestDistance = float.MaxValue;

		foreach (BaseNode node in Nodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

			float distance = node.GlobalPosition.DistanceSquaredTo(target.GlobalPosition);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = node;
			}
		}

		return best;
	}

	/// <summary>Shows the shared aim arrow (and power meter) for the planned shot.</summary>
	private void Preview(float powerPercent)
	{
		GameManager.Instance?.ShowAimPreview(ActionNode, _plannedAimAngle, powerPercent, true);
	}
}

using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;
using MbcPrototype.Rules;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Combat;

/// <summary>
/// The ammo of a weapon: it flies ballistically, and where it goes off it
/// damages everything near the impact by distance.
///
/// Every number it needs — damage, both blast radii, and the falloff curve
/// between them — is data (docs/parity §2, §7). Nothing here is tuned in code,
/// so a balance change is an edit to a <see cref="UnitDefinition"/> or to the
/// match's <see cref="MatchRules"/> and not a rebuild.
/// </summary>
public partial class Bomb : CharacterBody3D, ILaunchedAmmo
{
	public float Gravity = 9.8f;
	[Export] public GpuParticles3D ExplosionParticles;

	/// <summary>
	/// Resolved when this bomb finishes exploding. Part of the
	/// turn-resolution system: the turn cannot end until this event resolves.
	/// </summary>
	public TurnEvent TurnEvent { get; private set; }

	/// <summary>The roster entry this shot was fired as: its damage and blast radii.</summary>
	public UnitDefinition Unit { get; private set; }

	/// <summary>The match's global rules: the blast falloff curve.</summary>
	public MatchRules Rules { get; private set; }

	private bool _exploded = false;

	/// <summary>Called once by <see cref="AmmoLauncher"/> before this node enters the tree.</summary>
	public void Initialize(AmmoLaunch launch)
	{
		Velocity = launch.Velocity;
		Unit = launch.Unit;
		Rules = launch.Rules;
		TurnEvent = launch.TurnEvent;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_exploded) return;

		Vector3 v = Velocity;
		v.Y -= Gravity * (float)delta;
		Velocity = v;

		// Any solid contact ends the flight and sets the blast off: the ground, a
		// chain node, anything. Which node it touched does not decide the damage
		// — the blast does, by distance (see ApplyBlastDamage), so a bomb that
		// lands beside a node still hurts it.
		if (MoveAndSlide())
		{
			Explode();
		}
	}

	private async void Explode()
	{
		if (_exploded) return;
		_exploded = true;

		// Damage before the visuals, so the health bars are already updated in the
		// frame the blast appears.
		ApplyBlastDamage();

		// Hide the bomb itself
		GetNode<MeshInstance3D>("MeshInstance3D").Visible = false;

		// Trigger the particles
		if (ExplosionParticles != null)
		{
			ExplosionParticles.Emitting = true;
		}

		// Expanding spheres over the same radii, plus the ground marker that shows
		// where they landed. Its own node, parented inside the scene, so it
		// outlives this bomb — the bomb frees itself below while the marker is
		// still fading.
		float direct = Unit?.DirectHitRadius ?? 0f;
		float outer = Unit?.OuterRadius ?? 0f;
		if (outer > 0f) BlastVisual.Spawn(this, GlobalPosition, direct, outer);

		// Wait for particles to finish (lifetime is 0.5s)
		await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
		QueueFree();

		TurnEvent?.MarkResolved();
	}

	/// <summary>
	/// Applies the blast to every node in range: full damage inside the unit's
	/// <see cref="UnitDefinition.DirectHitRadius"/>, the match's falloff share of
	/// it out to its <see cref="UnitDefinition.OuterRadius"/>, nothing past it.
	///
	/// Distance is measured along the ground (XZ), which is what the ground marker
	/// draws: a bomb rests on or against whatever it hit, so its own height above
	/// the ground is not part of how close a node was. The blast is
	/// indiscriminate — it damages the firing combatant's own chain as readily as
	/// the target's, exactly as the old collision-based hit did.
	/// </summary>
	private void ApplyBlastDamage()
	{
		if (Unit == null || Rules == null)
		{
			// Only reachable if a bomb was spawned outside the launch path; inside
			// a match both are always supplied.
			GD.PushError("Bomb: launched without a unit definition or match rules — no damage dealt.");
			return;
		}

		int directDamage = Unit.Damage;
		float direct = Mathf.Max(0f, Unit.DirectHitRadius);
		float outer = Mathf.Max(direct, Unit.OuterRadius);
		if (directDamage <= 0 || outer <= 0f) return;

		int outerDamage = Rules.OuterBandDamage(directDamage);

		// A shape query finds the candidates without a node registry to keep in
		// step. Explode() runs from _PhysicsProcess, which is where physics queries
		// are safe to make. Nothing the blast can damage is missed: a node at
		// `outer` is within `outer` plus its own radius of the query sphere.
		// Which band it lands in comes from the distance measured below.
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = new SphereShape3D { Radius = outer },
			// PhysicsShapeQueryParameters3D defaults to the world origin, which
			// would query the wrong place entirely.
			Transform = new Transform3D(Basis.Identity, GlobalPosition),
			CollideWithBodies = true,
			CollideWithAreas = false,
		};

		// A body with several collision shapes comes back once per shape. Damage
		// must not depend on how many shapes a node happens to be built from.
		var hitNodes = new HashSet<BaseNode>();

		foreach (Godot.Collections.Dictionary hit in GetWorld3D().DirectSpaceState.IntersectShape(query))
		{
			// AsGodotObject, not As<BaseNode>: the result holds plain bodies too
			// (the ground), and a hard cast on one of those throws instead of
			// failing to match.
			if (hit["collider"].AsGodotObject() is not BaseNode node) continue;
			// A node already destroyed this frame is still in the physics space
			// (QueueFree lands at the end of the frame), so check the flag too.
			if (!IsInstanceValid(node) || node.IsDestroyed) continue;
			if (!hitNodes.Add(node)) continue;

			Vector3 offset = node.GlobalPosition - GlobalPosition;
			float distance = new Vector2(offset.X, offset.Z).Length();

			int damage = distance <= direct ? directDamage
				: distance <= outer ? outerDamage
				: 0;

			if (damage > 0) node.TakeDamage(damage);
		}
	}
}

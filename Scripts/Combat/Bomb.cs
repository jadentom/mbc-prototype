using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Combat;

public partial class Bomb : CharacterBody3D
{
	public float Gravity = 9.8f;
	[Export] public GpuParticles3D ExplosionParticles;

	/// <summary>
	/// Radius of the full-damage ("direct hit") zone, in world units, measured
	/// along the ground from where the bomb went off.
	///
	/// Before it had a blast at all, a bomb only ever damaged the node it
	/// physically touched, so the area it could hurt was about one node wide
	/// (nodes are 1-unit-wide cylinders). It started at twice that and was tuned
	/// down to 1.5 after watching the ground marker in play; <see cref="OuterRadius"/>
	/// stayed at 3, so the half-damage donut is what got wider. These two radii are
	/// also what <see cref="BlastVisual"/> draws and what its ground marker shows,
	/// so they can be compared against the health bars after a shot.
	/// </summary>
	[ExportGroup("Blast")]
	[Export] public float DirectHitRadius = 1.5f;

	/// <summary>
	/// Radius of the half-damage donut's outer edge, in world units. Nodes
	/// further out than this take nothing from the blast.
	/// </summary>
	[Export] public float OuterRadius = 3.0f;

	/// <summary>
	/// Damage dealt inside <see cref="DirectHitRadius"/>: the bomb's old
	/// one-point hit, doubled along with node health (see BaseNode.MaxHealth), so
	/// a node still takes exactly three direct bombs.
	/// </summary>
	[Export] public int DirectHitDamage = 2;

	/// <summary>Damage dealt in the donut between the two radii — half of a direct hit.</summary>
	[Export] public int HalfDamage = 1;

	/// <summary>
	/// Resolved when this bomb finishes exploding. Part of the
	/// turn-resolution system: the turn cannot end until this event resolves.
	/// </summary>
	public TurnEvent TurnEvent;

	private bool _exploded = false;

	public override void _PhysicsProcess(double delta)
	{
		if (_exploded) return;

		Vector3 v = Velocity;
		v.Y -= Gravity * (float)delta;
		Velocity = v;

		// Any solid contact ends the flight and sets the blast off: the ground, a
		// chain node, anything. Which node it touched no longer decides the damage
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
		BlastVisual.Spawn(this, GlobalPosition, DirectHitRadius, OuterRadius);

		// Wait for particles to finish (lifetime is 0.5s)
		await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
		QueueFree();

		TurnEvent?.MarkResolved();
	}

	/// <summary>
	/// Applies the blast to every node in range: full damage inside
	/// <see cref="DirectHitRadius"/>, <see cref="HalfDamage"/> out to
	/// <see cref="OuterRadius"/>, nothing past it.
	///
	/// Distance is measured along the ground (XZ), which is what the ground marker
	/// draws: a bomb rests on or against whatever it hit, so its own height above
	/// the ground is not part of how close a node was. The blast is
	/// indiscriminate — it damages the firing combatant's own chain as readily as
	/// the target's, exactly as the old collision-based hit did.
	/// </summary>
	private void ApplyBlastDamage()
	{
		float direct = Mathf.Max(0f, DirectHitRadius);
		float outer = Mathf.Max(direct, OuterRadius);
		if (outer <= 0f) return;

		// A shape query finds the candidates without a node registry to keep in
		// step. Explode() runs from _PhysicsProcess, which is where physics queries
		// are safe to make. Nothing the blast can damage is missed: a node is only
		// damaged out to `outer`, and at that distance its body still overlaps the
		// query sphere (the sphere is centered on the bomb, a node at `outer` is
		// within `outer` plus the node's own radius). Which band it lands in comes
		// from the distance measured below.
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

			int damage = distance <= direct ? DirectHitDamage
				: distance <= outer ? HalfDamage
				: 0;

			if (damage > 0) node.TakeDamage(damage);
		}
	}
}

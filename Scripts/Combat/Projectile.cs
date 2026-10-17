using Godot;
using MbcPrototype.Core;
using MbcPrototype.Rules;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Combat;

/// <summary>
/// The ammo of a unit whose shot deploys a structure: it flies ballistically,
/// and where it lands it puts a new node into the firing combatant's chain.
///
/// Which unit it is deploying is data, not a class: this scene is what a Hub's
/// <see cref="UnitDefinition.ProjectileScene"/> points at, and the definition it
/// is launched with decides the hit points of the structure it leaves behind.
/// How it flies is data too: the gravity that pulls it down is the match's
/// (docs/parity §7), so there is no fall rate in this file.
/// </summary>
public partial class Projectile : CharacterBody3D, ILaunchedAmmo
{
	[Export] public PackedScene BaseScene = GD.Load<PackedScene>("res://Scenes/structure.tscn");

	/// <summary>The structure the shot was fired from; null for a shot whose launcher is gone.</summary>
	public Structure CreatorStructure { get; private set; }

	/// <summary>The roster entry this shot was fired as: what the deployed node becomes.</summary>
	public UnitDefinition Unit { get; private set; }

	/// <summary>
	/// The match's global rules, which is where the flight comes from: gravity is
	/// read from here every step (docs/parity §7), so a rules resource edited
	/// under a running match changes the arcs in flight.
	/// </summary>
	public MatchRules Rules { get; private set; } = MatchRules.Fallback;

	/// <summary>
	/// Resolved when this projectile finishes its work (lands and deploys).
	/// Part of the turn-resolution system: the turn cannot end until this
	/// event resolves.
	/// </summary>
	public TurnEvent TurnEvent { get; private set; }

	/// <summary>Called once by <see cref="AmmoLauncher"/> before this node enters the tree.</summary>
	public void Initialize(AmmoLaunch launch)
	{
		Velocity = launch.Velocity;
		CreatorStructure = launch.Origin;
		Unit = launch.Unit;
		Rules = launch.Rules ?? MatchRules.Fallback;
		TurnEvent = launch.TurnEvent;
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 v = Velocity;
		v.Y -= Rules.EffectiveGravity * (float)delta;
		Velocity = v;

		if (MoveAndSlide())
		{
			Deploy();
		}
	}

	private void Deploy()
	{
		var newStructure = BaseScene.Instantiate<Structure>();
		// Set before AddChild: the structure reads its hit points off the
		// definition and builds its cord in _Ready. The spawn container sits at
		// the origin, so the local transform is the global one (the same
		// assumption SpawnRoot makes when it places a root).
		newStructure.Position = GlobalPosition;
		newStructure.Definition = Unit;

		// Only link into the chain while the creator is still alive. The new
		// structure joins the creator's chain (so it belongs to the same combatant
		// and keeps that combatant alive) and hangs from it: the cord between the
		// two is built by the structure in _Ready, because both ends have to be in
		// the tree before it can be measured.
		if (IsInstanceValid(CreatorStructure) && !CreatorStructure.IsDestroyed)
		{
			newStructure.ParentStructure = CreatorStructure;
			newStructure.OwnerChain = CreatorStructure.OwnerChain;
		}
		// A structure whose creator died while the shot was in flight has no chain
		// to join: it spawns unowned, so it neither belongs to a combatant nor
		// keeps a defeated chain alive (destroying a chain's root is that chain's
		// loss).

		// Parented inside the scene, never the tree root: this node outlives the
		// match otherwise — a reload frees the scene alone — and would come back
		// as a stale, unselectable leftover after the game-over restart.
		Node parent = GameManager.Instance?.SpawnContainer;
		if (parent == null) parent = GetTree().Root;
		parent.AddChild(newStructure);

		QueueFree();

		TurnEvent?.MarkResolved();
	}
}

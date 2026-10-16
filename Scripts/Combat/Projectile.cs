using Godot;
using MbcPrototype.Core;
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
/// </summary>
public partial class Projectile : CharacterBody3D, ILaunchedAmmo
{
	[Export] public PackedScene BaseScene = GD.Load<PackedScene>("res://Scenes/base_node.tscn");
	public float Gravity = 9.8f;

	/// <summary>The structure the shot was fired from; null for a shot whose launcher is gone.</summary>
	public BaseNode CreatorNode { get; private set; }

	/// <summary>The roster entry this shot was fired as: what the deployed node becomes.</summary>
	public UnitDefinition Unit { get; private set; }

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
		CreatorNode = launch.Origin;
		Unit = launch.Unit;
		TurnEvent = launch.TurnEvent;
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 v = Velocity;
		v.Y -= Gravity * (float)delta;
		Velocity = v;

		if (MoveAndSlide())
		{
			Deploy();
		}
	}

	private void Deploy()
	{
		var newBase = BaseScene.Instantiate<BaseNode>();
		// Set before AddChild: the node's _Ready() measures the cable to its
		// parent from this transform, and reads its hit points off the
		// definition below. The spawn container sits at the origin, so the local
		// transform is the global one (the same assumption SpawnRoot makes when
		// it places a root).
		newBase.Position = GlobalPosition;
		newBase.Definition = Unit;

		// Only link into the chain while the creator is still alive. The new node
		// joins the creator's chain (so it belongs to the same combatant and keeps
		// that combatant alive).
		if (IsInstanceValid(CreatorNode) && !CreatorNode.IsDestroyed)
		{
			newBase.ParentBase = CreatorNode;
			newBase.OwnerChain = CreatorNode.OwnerChain;
			CreatorNode.Children.Add(newBase);
		}
		// A node whose creator died while the shot was in flight has no chain to
		// join: it spawns unowned, so it neither belongs to a combatant nor keeps
		// a defeated chain alive (destroying a chain's root is that chain's loss).

		// Parented inside the scene, never the tree root: this node outlives the
		// match otherwise — a reload frees the scene alone — and would come back
		// as a stale, unselectable leftover after the game-over restart.
		Node parent = GameManager.Instance?.SpawnContainer;
		if (parent == null) parent = GetTree().Root;
		parent.AddChild(newBase);

		QueueFree();

		TurnEvent?.MarkResolved();
	}
}

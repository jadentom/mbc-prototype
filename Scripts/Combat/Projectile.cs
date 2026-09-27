using Godot;
using MbcPrototype.Core;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Combat;

public partial class Projectile : CharacterBody3D
{
	[Export] public PackedScene BaseScene = GD.Load<PackedScene>("res://Scenes/base_node.tscn");
	public float Gravity = 9.8f;
	public BaseNode CreatorNode;

	/// <summary>
	/// Resolved when this projectile finishes its work (lands and deploys).
	/// Part of the turn-resolution system: the turn cannot end until this
	/// event resolves.
	/// </summary>
	public TurnEvent TurnEvent;

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
		// parent from this transform. The spawn container sits at the origin, so
		// the local transform is the global one (the same assumption SpawnRoot
		// makes when it places a root).
		newBase.Position = GlobalPosition;

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

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
		newBase.Position = GlobalPosition; 

		// Only link into the chain while the creator is still alive.
		if (IsInstanceValid(CreatorNode) && !CreatorNode.IsDestroyed)
		{
			newBase.ParentBase = CreatorNode;
			CreatorNode.Children.Add(newBase);
		}
		
		GetTree().Root.AddChild(newBase);

		QueueFree();

		TurnEvent?.MarkResolved();
	}
}

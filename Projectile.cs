using Godot;

public partial class Projectile : CharacterBody3D
{
	[Export] public PackedScene BaseScene = GD.Load<PackedScene>("res://base_node.tscn");
	public float Gravity = 9.8f;
	public BaseNode CreatorNode;

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
		newBase.ParentBase = CreatorNode;
		newBase.Position = GlobalPosition; 
		
		GetTree().Root.AddChild(newBase);
		CreatorNode.Children.Add(newBase);
		
		QueueFree();
	}
}

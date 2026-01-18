using Godot;

public partial class Bomb : CharacterBody3D
{
	public float Gravity = 9.8f;
	[Export] public GpuParticles3D ExplosionParticles;

	private bool _exploded = false;

	public override void _PhysicsProcess(double delta)
	{
		if (_exploded) return;

		Vector3 v = Velocity;
		v.Y -= Gravity * (float)delta;
		Velocity = v;

		if (MoveAndSlide())
		{
			var collision = GetLastSlideCollision();
			if (collision != null)
			{
				var collider = collision.GetCollider();
				if (collider is BaseNode node)
				{
					node.TakeDamage(1);
				}
			}
			Explode();
		}
	}

	private async void Explode()
	{
		if (_exploded) return;
		_exploded = true;

		// Hide the bomb itself
		GetNode<MeshInstance3D>("MeshInstance3D").Visible = false;
		
		// Trigger the particles
		if (ExplosionParticles != null)
		{
			ExplosionParticles.Emitting = true;
		}

		// Wait for particles to finish (lifetime is 0.5s)
		await ToSignal(GetTree().CreateTimer(0.6f), "timeout");
		QueueFree();
	}
}

using Godot;

public partial class Bomb : CharacterBody3D
{
	public float Gravity = 9.8f;
	[Export] public GpuParticles3D ExplosionParticles;

	public override void _PhysicsProcess(double delta)
	{
		Vector3 v = Velocity;
		v.Y -= Gravity * (float)delta;
		Velocity = v;

		if (MoveAndSlide())
		{
			Explode();
		}
	}

	private async void Explode()
	{
		if (!Visible) return; 

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

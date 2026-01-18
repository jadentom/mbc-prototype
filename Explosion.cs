using Godot;

public partial class Explosion : Node3D
{
    private MeshInstance3D _sphere;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        _sphere = GetNode<MeshInstance3D>("MeshInstance3D");
        _material = (StandardMaterial3D)_sphere.GetActiveMaterial(0)?.Duplicate();
        _sphere.SetSurfaceOverrideMaterial(0, _material);

        var tween = GetTree().CreateTween();
        tween.SetParallel(true);

        // Animate scale
        tween.TweenProperty(_sphere, "scale", Vector3.One * 5, 0.5f)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);

        // Animate fade out
        tween.TweenProperty(_material, "albedo_color:a", 0.0f, 0.5f);

        tween.Finished += () => QueueFree();
    }
}

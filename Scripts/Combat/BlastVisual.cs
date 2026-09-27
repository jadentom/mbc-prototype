using Godot;
using System.Collections.Generic;
using MbcPrototype.Core;

namespace MbcPrototype.Combat;

/// <summary>
/// The visual half of a bomb's blast: one translucent sphere per damage radius,
/// expanding out of the blast and fading, plus a ground marker that keeps
/// showing where those radii actually landed after the flash is gone.
///
/// Nothing here decides damage — <see cref="Bomb"/> owns the radii and applies
/// them, and this draws the same numbers, which is the only reason the marker
/// is worth looking at.
///
/// Built in code rather than as a scene: every number it needs comes from the
/// bomb that spawned it, so a scene could only hold duplicates of them.
/// </summary>
public partial class BlastVisual : Node3D
{
	/// <summary>Radius of the full-damage zone: the filled disc and the inner sphere.</summary>
	public float DirectHitRadius = 2.0f;

	/// <summary>Radius of the half-damage donut's outer edge: the outer ring and sphere.</summary>
	public float OuterRadius = 3.0f;

	// --- Visual tuning only; none of this changes what the blast damages. ---

	/// <summary>Seconds the spheres take to grow from nothing to their radius.</summary>
	public float SphereExpandTime = 0.35f;

	/// <summary>Seconds the spheres take to fade, starting once they have grown.</summary>
	public float SphereFadeTime = 0.35f;

	/// <summary>Seconds the ground marker holds at full opacity before it fades.</summary>
	public float MarkerHoldTime = 1.4f;

	/// <summary>Seconds the ground marker takes to fade out.</summary>
	public float MarkerFadeTime = 0.6f;

	/// <summary>Where the ground marker is drawn: just above the ground plane, clear of the ground mesh.</summary>
	private const float MarkerHeight = 0.03f;

	/// <summary>Thickness of a marker disc (and of the torus tube a ring is made of).</summary>
	private const float MarkerDiscHeight = 0.02f;
	private const float MarkerRingThickness = 0.12f;

	/// <summary>How far up the marker's stack the rings sit, above both discs.</summary>
	private const float MarkerRingHeight = 0.06f;

	/// <summary>The sphere starts here and grows to its radius: the growth is the animation.</summary>
	private const float SphereStartScale = 0.02f;

	// Translucent on purpose — the blast is a volume you look through, and a solid
	// ball would hide the nodes it is drawn around. The direct-hit band is the
	// brighter of the two, so which radius is which stays readable when they
	// overlap.
	private static readonly Color DirectHitColor = new Color(1.0f, 0.45f, 0.1f);
	private static readonly Color OuterColor = new Color(1.0f, 0.85f, 0.3f);

	private const float DirectSphereAlpha = 0.28f;
	private const float OuterSphereAlpha = 0.13f;
	private const float DirectDiscAlpha = 0.22f;
	private const float OuterDiscAlpha = 0.10f;
	private const float DirectRingAlpha = 0.90f;
	private const float OuterRingAlpha = 0.55f;

	// Every material the ground marker uses, so the whole marker fades together.
	private readonly List<StandardMaterial3D> _markerMaterials = new List<StandardMaterial3D>();

	/// <summary>
	/// Spawns the effect for a bomb that went off at <paramref name="center"/> and
	/// returns it. The caller does not have to keep it: it frees itself once the
	/// ground marker has faded.
	/// </summary>
	/// <param name="caller">
	/// A node already in the tree, used only for the fallback parent before the
	/// match has been set up.
	/// </param>
	/// <param name="center">Where the bomb went off.</param>
	/// <param name="directHitRadius">Full-damage radius the effect has to draw.</param>
	/// <param name="outerRadius">Outer, half-damage radius the effect has to draw.</param>
	public static BlastVisual Spawn(Node caller, Vector3 center, float directHitRadius, float outerRadius)
	{
		var visual = new BlastVisual
		{
			Name = "BlastVisual",
			DirectHitRadius = directHitRadius,
			OuterRadius = outerRadius,
		};

		// Parented inside the scene, never the tree root: the marker deliberately
		// outlives the bomb that made it, and anything under the tree root would
		// outlive the match's scene reload too (see GameManager.SpawnContainer).
		Node parent = GameManager.Instance?.SpawnContainer;
		if (parent == null) parent = caller.GetTree().Root;
		parent.AddChild(visual);

		visual.GlobalPosition = center;
		// Built only once the position is known: the ground marker has to land on
		// the ground plane, not wherever the bomb happened to be above it.
		visual.Build();
		return visual;
	}

	private void Build()
	{
		// A radius of zero draws nothing readable, and an outer radius inside the
		// direct-hit one would mean a half-damage band that cannot be reached.
		float direct = Mathf.Max(0.01f, DirectHitRadius);
		float outer = Mathf.Max(direct, OuterRadius);

		BuildSpheres(direct, outer);
		BuildGroundMarker(direct, outer);
	}

	/// <summary>
	/// One translucent sphere per damage radius, expanding out of the blast
	/// centre and fading: the volume half of "how big was that".
	/// </summary>
	private void BuildSpheres(float direct, float outer)
	{
		var root = new Node3D { Name = "Spheres" };
		AddChild(root);

		MeshInstance3D directSphere = AddSphere(root, DirectHitColor, DirectSphereAlpha);
		MeshInstance3D outerSphere = AddSphere(root, OuterColor, OuterSphereAlpha);

		// Parallel: both radii grow together, so the two bands stay comparable
		// while they are moving.
		Tween tween = CreateTween();
		tween.SetParallel(true);
		ExpandAndFade(tween, directSphere, direct);
		ExpandAndFade(tween, outerSphere, outer);
	}

	/// <summary>
	/// Adds one unit sphere, translucent, starting at almost no size: the tween in
	/// <see cref="ExpandAndFade"/> grows its scale to the blast radius.
	/// </summary>
	private MeshInstance3D AddSphere(Node3D parent, Color color, float alpha)
	{
		var sphere = new MeshInstance3D
		{
			Name = "BlastSphere",
			// Radius 1 / height 2 is a unit sphere, so scale == radius.
			Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f },
			MaterialOverride = BlastMaterial(color, alpha),
			Scale = Vector3.One * SphereStartScale,
		};
		parent.AddChild(sphere);
		return sphere;
	}

	/// <summary>
	/// Makes one unit sphere grow to <paramref name="radius"/> and then fade out.
	/// </summary>
	private void ExpandAndFade(Tween tween, MeshInstance3D sphere, float radius)
	{
		// The mesh is a unit sphere, so a tween on "scale" is a tween on the radius.
		tween.TweenProperty(sphere, "scale", Vector3.One * radius, SphereExpandTime)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);

		// The fade waits for the growth to finish: a sphere that goes transparent
		// while it is still small reads as a pop instead of an expanding blast.
		var material = sphere.GetActiveMaterial(0) as StandardMaterial3D;
		tween.TweenProperty(material, "albedo_color:a", 0.0f, SphereFadeTime)
			.SetDelay(SphereExpandTime);
	}

	/// <summary>
	/// Draws the two radii on the ground: a translucent disc filling the
	/// full-damage zone, and a ring at each radius. This is what is left on the
	/// ground once the flash is gone, so a node that took damage — or did not —
	/// can be compared against the bands that were supposed to reach it.
	/// </summary>
	private void BuildGroundMarker(float direct, float outer)
	{
		var marker = new Node3D
		{
			Name = "GroundMarker",
			// The blast centre is the bomb's position, which sits above the ground,
			// and the radii are measured along the ground (Bomb.ApplyBlastDamage),
			// so the marker drops to the ground plane instead of following the bomb
			// up. The spawn container sits at the origin with an identity transform,
			// so a local offset here is a world offset.
			Position = new Vector3(0f, MarkerHeight - GlobalPosition.Y, 0f),
		};
		AddChild(marker);

		// Stacked in Y so no two translucent surfaces are coplanar: coincident
		// alpha-blended surfaces z-fight into a flicker.
		AddDisc(marker, outer, OuterColor, OuterDiscAlpha, 0f);
		AddDisc(marker, direct, DirectHitColor, DirectDiscAlpha, MarkerDiscHeight * 2f);
		AddRing(marker, direct, DirectHitColor, DirectRingAlpha);
		AddRing(marker, outer, OuterColor, OuterRingAlpha);

		FadeMarkerAndFree();
	}

	/// <summary>Adds a flat disc of exactly <paramref name="radius"/>, at <paramref name="height"/> above the marker's base.</summary>
	private void AddDisc(Node3D parent, float radius, Color color, float alpha, float height)
	{
		var disc = new MeshInstance3D
		{
			Name = "BlastDisc",
			Mesh = new CylinderMesh
			{
				TopRadius = radius,
				BottomRadius = radius,
				Height = MarkerDiscHeight,
			},
			MaterialOverride = BlastMaterial(color, alpha),
			Position = new Vector3(0f, height, 0f),
		};
		parent.AddChild(disc);
		_markerMaterials.Add((StandardMaterial3D)disc.MaterialOverride);
	}

	/// <summary>
	/// Adds a ring marking a damage radius, as a torus lying flat on the ground
	/// plane so that seen from above it is a circle of exactly that radius.
	/// </summary>
	private void AddRing(Node3D parent, float radius, Color color, float alpha)
	{
		float tube = MarkerRingThickness * 0.5f;
		var ring = new MeshInstance3D
		{
			Name = "BlastRing",
			Mesh = new TorusMesh
			{
				InnerRadius = Mathf.Max(0.01f, radius - tube),
				OuterRadius = radius + tube,
			},
			MaterialOverride = BlastMaterial(color, alpha),
			Position = new Vector3(0f, MarkerRingHeight, 0f),
		};
		parent.AddChild(ring);
		_markerMaterials.Add((StandardMaterial3D)ring.MaterialOverride);
	}

	/// <summary>
	/// Translucent, unshaded and unlit: a blast should read as a coloured volume
	/// rather than as a lit ball. Backfaces are drawn as well, so the far wall of
	/// a sphere shows through the near one and the shape reads as a volume.
	/// </summary>
	private static StandardMaterial3D BlastMaterial(Color color, float alpha)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = new Color(color.R, color.G, color.B, alpha),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}

	/// <summary>
	/// Holds the marker for <see cref="MarkerHoldTime"/>, fades every material it
	/// uses, then frees the whole effect. The tween is bound to this node (created
	/// with <c>CreateTween</c>), so a scene reload part-way through kills the tween
	/// with the node instead of leaving a callback pointed at freed nodes.
	/// </summary>
	private void FadeMarkerAndFree()
	{
		Tween tween = CreateTween();
		tween.TweenInterval(MarkerHoldTime);

		bool first = true;
		foreach (StandardMaterial3D material in _markerMaterials)
		{
			// The first fade follows the hold; the rest run alongside it.
			Tween fade = first ? tween : tween.Parallel();
			fade.TweenProperty(material, "albedo_color:a", 0.0f, MarkerFadeTime);
			first = false;
		}

		// The marker is the last thing on screen, so a finished effect is a whole
		// finished node — and this one lives under the spawn container, where
		// nothing else would ever clean it up.
		tween.Finished += () => QueueFree();
	}
}

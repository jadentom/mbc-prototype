using Godot;
using System.Collections.Generic;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Core;

public partial class BaseNode : StaticBody3D
{
	[Export] public PackedScene ExplosionScene;

	public BaseNode ParentBase;
	public List<BaseNode> Children = new List<BaseNode>();

	/// <summary>
	/// The combatant whose chain this node belongs to. Set before the node
	/// enters the tree — by the projectile that deployed it, or by the
	/// combatant spawning its root — so _Ready can register it with that chain.
	/// Null for a node that never made it into a chain (see Projectile.Deploy),
	/// which belongs to nobody and keeps nobody alive.
	/// </summary>
	public NodeChainCombatant OwnerChain { get; set; }

	private MeshInstance3D _highlight;
	private ProgressBar _healthBar;
	private SubViewport _viewport;

	// Health bar pixels. The SubViewport is built at this size and the dividers
	// below are laid out against it, so the two stay in step; Scenes/health_bar.tscn
	// holds a Control of the same size.
	private const int HealthBarWidth = 80;
	private const int HealthBarHeight = 10;

	/// <summary>Width of a health-bar divider, in bar pixels.</summary>
	private const int HealthBarTickWidth = 2;

	/// <summary>
	/// Divider colour. Light rather than black so that one line per point of
	/// health is readable over the red fill and over the empty part of the bar
	/// alike — the bar is small on screen, and black lines vanish into the
	/// background.
	/// </summary>
	private static readonly Color HealthBarTickColor = new Color(1f, 1f, 1f, 0.6f);

	/// <summary>
	/// True once this node has been marked for destruction. Remains true until
	/// the node actually leaves the tree (QueueFree defers the removal to the
	/// end of the frame), so callers can reliably tell a dying node from a
	/// living one even before it is freed.
	/// </summary>
	public bool IsDestroyed { get; private set; } = false;

	/// <summary>
	/// Resolved once this node has actually left the tree after destruction.
	/// Keeps the turn resolution running until the whole destruction cascade
	/// (this node and every descendant) has finished.
	/// </summary>
	private TurnEvent _destructionEvent;

	/// <summary>
	/// Health a fresh node spawns with. Six points rather than the three this
	/// node used to have: a bomb's half-damage ring has to be a whole point (see
	/// Bomb.HalfDamage), and a bomb's direct hit was doubled along with it, so a
	/// node still takes exactly three direct bombs.
	/// </summary>
	[Export] public int MaxHealth = 6;

	/// <summary>Current health. Starts at <see cref="MaxHealth"/> and bottoms out at zero.</summary>
	public int Health { get; private set; }

	public override void _Ready()
	{
		Health = MaxHealth;

		_highlight = GetNode<MeshInstance3D>("HighlightRing");
		_highlight.Visible = false;

		// In 3D, we use _InputEvent for mouse clicks on objects
		InputEvent += OnInput;

		// If we have a parent, create the visual connection
		if (ParentBase != null)
		{
			CreateCable();
		}

		// Health Bar
		var healthBarScene = GD.Load<PackedScene>("res://Scenes/health_bar.tscn");
		var healthBarInstance = healthBarScene.Instantiate<Control>();

		_viewport = new SubViewport();
		_viewport.Size = new Vector2I(HealthBarWidth, HealthBarHeight);
		_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_viewport.TransparentBg = true;
		_viewport.AddChild(healthBarInstance);
		AddChild(_viewport);

		var sprite = new Sprite3D();
		sprite.Texture = _viewport.GetTexture();
		sprite.Position = new Vector3(0, 2, 0);
		sprite.Scale = new Vector3(3, 3, 1);
		sprite.Centered = true;
		sprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		AddChild(sprite);

		_healthBar = healthBarInstance.GetNode<ProgressBar>("ProgressBar");
		// Added after the bar itself, so the dividers draw on top of the fill.
		CreateHealthBarTicks(healthBarInstance);
		UpdateHealthBar();

		// The owning chain (which decides this combatant's defeat) and the global
		// registry (which drives the minimap) are tracked separately.
		OwnerChain?.RegisterNode(this);
		GameManager.Instance?.RegisterNode(this);
	}

	public override void _ExitTree()
	{
		// Resolve the destruction event only once this node is truly out of
		// the tree, so the turn doesn't end while the cascade is still running.
		if (_destructionEvent != null)
		{
			_destructionEvent.MarkResolved();
			_destructionEvent = null;
		}

		// The chain may already be gone when a scene teardown frees everything at
		// once, so only unregister from an owner that is still alive.
		if (OwnerChain != null && IsInstanceValid(OwnerChain))
		{
			OwnerChain.UnregisterNode(this);
		}
		GameManager.Instance?.UnregisterNode(this);
		base._ExitTree();
	}

	private void CreateCable()
	{
		if (ParentBase == null) return;

		MeshInstance3D cable = new MeshInstance3D();
		BoxMesh mesh = new BoxMesh();

		float distance = GlobalPosition.DistanceTo(ParentBase.GlobalPosition);
		mesh.Size = new Vector3(0.1f, 0.1f, distance);
		mesh.SubdivideDepth = (int)Mathf.Max(10, distance * 2);
		cable.Mesh = mesh;

		ShaderMaterial mat = new ShaderMaterial();
		mat.Shader = GD.Load<Shader>("res://Shaders/CableShader.gdshader");

		// This is the critical line that fixes the "middle-out" issue
		mat.SetShaderParameter("cable_length", distance);

		cable.MaterialOverride = mat;
		AddChild(cable);

		// Position and Orientation
		cable.Position = Vector3.Zero;
		cable.LookAt(ParentBase.GlobalPosition);

		// Move the box so its start sits at the child node
		cable.Position = -cable.Transform.Basis.Z * (distance / 2.0f);
	}

	private void OnInput(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			GameManager.Instance?.SelectNode(this);
		}
	}

	/// <summary>
	/// Toggles the selection ring. Safe before this node's _Ready has built the
	/// ring (selection can happen in the same frame the node is created).
	/// </summary>
	public void SetHighlight(bool state)
	{
		if (_highlight != null) _highlight.Visible = state;
	}

	public void TakeDamage(int amount)
	{
		if (amount <= 0) return;

		Health = Mathf.Max(0, Health - amount);
		UpdateHealthBar();
		if (Health <= 0)
		{
			Destroy();
		}
	}

	public async void Destroy()
	{
		if (IsDestroyed) return;
		IsDestroyed = true;

		// This node's destruction (and the cascade it triggers) must resolve
		// before the current turn can end. Resolved in _ExitTree, once the
		// node has actually left the tree.
		_destructionEvent = new TurnEvent("Destroy " + Name);
		GameManager.Instance?.RegisterTurnEvent(_destructionEvent);

		// Instantiate explosion immediately
		if (ExplosionScene != null)
		{
			var explosion = ExplosionScene.Instantiate<Node3D>();

			// Parented inside the scene, never the tree root: an explosion still
			// playing when the match ends would outlive the restart's scene reload.
			Node parent = GameManager.Instance?.SpawnContainer;
			if (parent == null) parent = GetTree().Root;
			parent.AddChild(explosion);
			explosion.GlobalPosition = this.GlobalPosition;
		}

		// Delay
		await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

		// Propagate destruction to children
		foreach (BaseNode child in Children)
		{
			if (IsInstanceValid(child)) // Check if child is still valid before destroying
			{
				child.Destroy(); // Recursively destroy children
			}
		}
		Children.Clear(); // Clear the list after triggering destruction

		// Actual removal (the destruction event resolves in _ExitTree)
		QueueFree();
	}

	/// <summary>
	/// Adds the divider line between every point of health, so a hit that took
	/// one point is easy to tell from one that took two.
	///
	/// Built here from <see cref="MaxHealth"/> rather than stored in the health
	/// bar scene: that scene is shared by every node, and how many lines a bar
	/// needs is a property of the node, not of the bar.
	/// </summary>
	private void CreateHealthBarTicks(Control healthBar)
	{
		if (MaxHealth <= 1) return;

		for (int point = 1; point < MaxHealth; point++)
		{
			var tick = new ColorRect
			{
				Name = $"HealthTick{point}",
				Color = HealthBarTickColor,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				// Laid out against the bar's own pixel width, not against
				// Control.Size: _Ready runs before the layout does.
				Position = new Vector2(Mathf.Round(point * HealthBarWidth / (float)MaxHealth), 0f),
				Size = new Vector2(HealthBarTickWidth, HealthBarHeight),
			};
			healthBar.AddChild(tick);
		}
	}

	private void UpdateHealthBar()
	{
		if (_healthBar == null) return;

		_healthBar.Value = MaxHealth > 0
			? Mathf.Clamp((float)Health / MaxHealth, 0f, 1f) * 100f
			: 0f;
	}
}

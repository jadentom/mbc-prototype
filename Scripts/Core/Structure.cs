using Godot;
using System.Collections.Generic;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;

namespace MbcPrototype.Core;

/// <summary>
/// A unit standing on the map: hit points, the combatant that owns it, the
/// selection ring and the health bar. It is one end of every cord it touches —
/// the cord it hangs from (<see cref="ParentCord"/>, at most one, because a
/// structure is built by exactly one shot) and the cords it anchors
/// (<see cref="ChildCords"/>, one per structure launched from it).
///
/// The other half of what this class used to be is <see cref="Cord"/> (docs/parity
/// M0.3): "the unit" and "the link in the network" are separate objects, so the
/// network can grow rules — reach, overlap, cord nodes, the cascade, lineage
/// lights — without every one of them being a method on a unit.
/// </summary>
public partial class Structure : StaticBody3D
{
	[Export] public PackedScene ExplosionScene;

	/// <summary>
	/// What this structure is, from the roster: its hit points today, and later
	/// its vision radius and what it may build. Authored on a structure placed in
	/// a scene, and stamped by the ammo that deploys one (see
	/// <see cref="MbcPrototype.Combat.Projectile.Deploy"/>) before the structure
	/// enters the tree, so <see cref="_Ready"/> can read it.
	/// </summary>
	[Export] public UnitDefinition Definition;

	/// <summary>
	/// The structure this one was launched from, or null for a root — and for a
	/// structure whose launcher died while its shot was in flight, which is why
	/// it is set (or left unset) before the structure enters the tree rather than
	/// derived from the cord below.
	///
	/// The cord between the two is built in <see cref="_Ready"/>: it needs both
	/// ends in the tree before it can measure itself.
	/// </summary>
	public Structure ParentStructure { get; set; }

	/// <summary>The cord to <see cref="ParentStructure"/>; null for a root.</summary>
	public Cord ParentCord { get; private set; }

	/// <summary>Every cord hanging from this structure, one per structure built from it.</summary>
	public IReadOnlyList<Cord> ChildCords => _childCords;

	private readonly List<Cord> _childCords = new List<Cord>();

	/// <summary>
	/// The combatant whose chain this structure belongs to. Set before the
	/// structure enters the tree — by the projectile that deployed it, or by the
	/// combatant spawning its root — so _Ready can register it with that chain.
	/// Null for a structure that never made it into a chain (see
	/// <see cref="MbcPrototype.Combat.Projectile.Deploy"/>), which belongs to
	/// nobody and keeps nobody alive.
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
	/// True once this structure has been marked for destruction. Remains true
	/// until it actually leaves the tree (QueueFree defers the removal to the end
	/// of the frame), so callers can reliably tell a dying structure from a
	/// living one even before it is freed.
	/// </summary>
	public bool IsDestroyed { get; private set; } = false;

	/// <summary>
	/// Resolved once this structure has actually left the tree after destruction.
	/// Keeps the turn resolution running until the whole destruction cascade
	/// (this structure and everything built from it) has finished.
	/// </summary>
	private TurnEvent _destructionEvent;

	/// <summary>
	/// Hit points this structure spawns with, taken from its
	/// <see cref="Definition"/> (docs/parity §7: the manual's <em>Armor</em>
	/// column, which is hit points, not a damage threshold).
	///
	/// The constant below is the fallback for a structure with no definition at
	/// all — the project must still run with no content (§2) — and is never the
	/// source of truth. A structure that is missing its definition is a content
	/// error and says so in <see cref="_Ready"/>.
	/// </summary>
	public int MaxHealth => Definition != null && Definition.HitPoints > 0
		? Definition.HitPoints
		: FallbackHitPoints;

	/// <summary>Hit points used by a structure whose definition is missing or has none of its own.</summary>
	private const int FallbackHitPoints = 5;

	/// <summary>Current health. Starts at <see cref="MaxHealth"/> and bottoms out at zero.</summary>
	public int Health { get; private set; }

	/// <summary>
	/// This structure's identity within its match, handed out when it registers:
	/// the roots first, then the structures shots deploy, in the order they land.
	///
	/// Commands name structures by this id (docs/parity M0.5), so it has to be the
	/// same number on every run of the same command log — which it is, because the
	/// simulation creates things in the same order every time.
	/// </summary>
	public int Id { get; private set; }

	/// <summary>Called by the match as this structure registers; an id is only ever assigned once.</summary>
	public void AssignId(int id)
	{
		if (Id == 0) Id = id;
	}

	public override void _Ready()
	{
		if (Definition == null || Definition.HitPoints <= 0)
		{
			GD.PushWarning($"{Name}: no UnitDefinition with hit points assigned — using the {FallbackHitPoints}-hit-point fallback.");
		}

		Health = MaxHealth;

		_highlight = GetNode<MeshInstance3D>("HighlightRing");
		_highlight.Visible = false;

		// In 3D, we use _InputEvent for mouse clicks on objects
		InputEvent += OnInput;

		// The link this structure hangs from: the cord is built here rather than
		// by the shot that deployed it, because both ends have to be in the tree
		// before their world positions can be measured.
		if (IsLive(ParentStructure))
		{
			ParentCord = Cord.Connect(ParentStructure, this);
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
		OwnerChain?.RegisterStructure(this);
		GameManager.Instance?.RegisterStructure(this);
	}

	public override void _ExitTree()
	{
		// Resolve the destruction event only once this structure is truly out of
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
			OwnerChain.UnregisterStructure(this);
		}
		GameManager.Instance?.UnregisterStructure(this);
		base._ExitTree();
	}

	/// <summary>Records a cord hanging from this structure (called by <see cref="Cord.Connect"/>).</summary>
	public void AddCord(Cord cord)
	{
		if (cord != null && !_childCords.Contains(cord)) _childCords.Add(cord);
	}

	/// <summary>Forgets a cord that is no longer there (called when a cord leaves the tree).</summary>
	public void RemoveCord(Cord cord)
	{
		_childCords.Remove(cord);
	}

	/// <summary>
	/// Toggles the selection ring. Safe before this structure's _Ready has built
	/// the ring (selection can happen in the same frame it is created).
	/// </summary>
	public void SetHighlight(bool state)
	{
		if (_highlight != null) _highlight.Visible = state;
	}

	/// <summary>
	/// Subtracts <paramref name="amount"/> from this structure's hit points, one
	/// point per point: there is no armour threshold, so partial damage is
	/// normal and a Bomb (3) on a Hub (5) leaves 2. Reaching zero destroys it.
	/// </summary>
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

		// This structure's destruction (and the cascade it triggers) must resolve
		// before the current turn can end. Resolved in _ExitTree, once this
		// structure has actually left the tree.
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

		// The cascade runs down the cords: everything built from this structure
		// hangs from one of them, including other Hubs, so this reaches the whole
		// subtree.
		foreach (Cord cord in _childCords)
		{
			Structure child = cord?.ChildStructure;
			if (child != null && IsInstanceValid(child))
			{
				child.Destroy(); // Recursively destroy what hangs below
			}
		}
		_childCords.Clear(); // Clear the list after triggering destruction

		// Actual removal (the destruction event resolves in _ExitTree)
		QueueFree();
	}

	/// <summary>
	/// Adds the divider line between every point of health, so a hit that took
	/// one point is easy to tell from one that took two.
	///
	/// Built here from <see cref="MaxHealth"/> rather than stored in the health
	/// bar scene: that scene is shared by every structure, and how many lines a
	/// bar needs is a property of the structure, not of the bar.
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

	private void OnInput(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			GameManager.Instance?.SelectStructure(this);
		}
	}

	/// <summary>True for a structure that is in the tree and not marked for destruction.</summary>
	private static bool IsLive(Structure structure)
	{
		return structure != null && GodotObject.IsInstanceValid(structure) && !structure.IsDestroyed;
	}
}

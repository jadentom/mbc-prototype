using Godot;
using System;
using System.Collections.Generic;
using MbcPrototype.Combat;
using MbcPrototype.Enemies;
using MbcPrototype.TurnSystem;

namespace MbcPrototype.Core;

public partial class GameManager : Node
{
	public static GameManager Instance;

	[ExportGroup("References")]
	[Export] public Camera3D MainCamera;
	[Export] public ProgressBar PowerBar;

	[ExportGroup("Aiming Settings")]
	[Export] public float RotationSpeed = 3.0f;
	[Export] public float UpwardBias = 5f;

	[ExportGroup("Power Settings")]
	[Export] public float MinLaunchForce = 4.0f;
	[Export] public float MaxLaunchForce = 20.0f;
	[Export] public float ChargeSpeed = 1.0f; // 1.0 = fills in 1 second

	[ExportGroup("Camera Settings")]
	[Export] public float CameraSmoothTime = 0.5f;

	[ExportGroup("Enemy")]
	/// <summary>How far right of the player's starting node the enemy chain spawns, in screen widths (1 screen = the camera's visible width).</summary>
	[Export] public float EnemyStartScreensRight = 2.0f;
	/// <summary>Seconds the enemy spends aiming before it releases its shot.</summary>
	[Export] public float EnemyThinkTime = 1.0f;
	/// <summary>Half-angle of the enemy's random aim cone, in degrees (180 = any direction).</summary>
	[Export] public float EnemyAimSpreadDegrees = 25.0f;
	/// <summary>Lowest launch power the enemy will pick, 0..1 of the force range.</summary>
	[Export(PropertyHint.Range, "0,1")] public float EnemyMinPower = 0.3f;
	/// <summary>Highest launch power the enemy will pick, 0..1 of the force range.</summary>
	[Export(PropertyHint.Range, "0,1")] public float EnemyMaxPower = 0.9f;
	/// <summary>Center the camera on whichever combatant is acting, so the enemy's move (it starts two screens away) is visible.</summary>
	[Export] public bool CenterCameraOnActingCombatant = true;

	[ExportGroup("Camera Panning")]
	[Export] public float PanSpeed = 60.0f;
	[Export] public int PanEdgeMargin = 48;
	[Export] public Vector2 PanBoundsMin = new Vector2(-5000, -5000);
	[Export] public Vector2 PanBoundsMax = new Vector2(5000, 5000);

	[ExportGroup("Minimap")]
	[Export] public Vector2 MinimapCenter = new Vector2(0, 0);
	// World side length shown. 1500^2 -> 475^2 cuts the covered area ~10x
	// (2250000 vs 225625) while keeping the on-screen widget small.
	[Export] public float MinimapWorldSize = 475.0f;
	// On-screen minimap side as a fraction of the window's shorter side
	// (e.g. 0.25 -> 162px at 648px window height, 270px at 1080p).
	[Export(PropertyHint.Range, "0.05, 0.5")]
	public float MinimapScreenFraction = 0.25f;
	[Export] public Color MinimapViewRectColor = new Color(1, 1, 1, 0.4f);
	[Export] public Vector2 MinimapMarkerSize = new Vector2(3, 3);
	[Export] public Color MinimapNodeColor = new Color(0, 0, 0, 0.9f);
	[Export] public Color MinimapEnemyNodeColor = new Color(0.9f, 0.1f, 0.1f, 0.95f);
	[Export] public Color MinimapSelectedNodeColor = new Color(1, 1, 0, 1);
	[Export] public Color MinimapLineColor = new Color(0, 0, 0, 0.7f);
	[Export] public float MinimapLineWidth = 1.0f;

	[ExportGroup("UI References")]
	[Export] public TextureRect NodeIcon;
	[Export] public TextureRect BombIcon;
	[Export] public ColorRect SelectorBox;
	[Export] public Label TurnLabel;
	[Export] public Label DefeatLabel;
	[Export] public Label VictoryLabel;
	[Export] public Label RestartLabel;

	[ExportGroup("Ammo Prefabs")]
	[Export] public PackedScene ProjectileScene; // TODO: Change this to an interface; this is specifically a deploying node projectile
	[Export] public PackedScene BombScene;

	public BaseNode SelectedNode;

	/// <summary>The combatant whose turn it currently is (null before the match starts).</summary>
	public Combatant ActiveCombatant { get; private set; }

	/// <summary>Every combatant in the match, in turn order. Read by the AI to find its targets.</summary>
	public IReadOnlyList<Combatant> Combatants => _combatants;

	/// <summary>True once the match has been won or lost; input restarts the scene.</summary>
	public bool IsGameOver { get; private set; } = false;

	/// <summary>
	/// Container that spawned chain nodes are parented under. A combatant may
	/// spawn its root while the GameManager is still being readied, and neither
	/// the tree root nor the scene root accepts children at that point ("parent
	/// node is busy setting up children"); a node's own children never are.
	/// </summary>
	public Node3D NodeContainer { get; private set; }

	/// <summary>
	/// Where everything spawned during a match is parented: ammo in flight,
	/// explosions, and the chain nodes ammo deploys (a combatant's own root is
	/// parented under <see cref="NodeContainer"/> directly).
	///
	/// Always a node *inside the scene*, never the tree root: restarting a
	/// finished match reloads the scene, and a reload frees the scene alone. A
	/// node parented to the tree root outlives the match and leaks into the next
	/// one — visible, unselectable, owned by a chain that no longer exists, and
	/// the first "BaseNode" in the tree, so the new match would adopt it as the
	/// player's starting node (see <see cref="SceneRoot"/>).
	///
	/// Falls back to the tree root only before the match has been set up.
	/// </summary>
	public Node SpawnContainer
	{
		get
		{
			if (IsInstanceValid(NodeContainer)) return NodeContainer;
			return GetTree()?.Root;
		}
	}

	/// <summary>
	/// The subtree this match lives in — the scene root a restart replaces
	/// (GameManager is a direct child of it). The player's starting node is
	/// looked up from here rather than from the tree root, so a node that lives
	/// outside the scene can never be mistaken for part of the match.
	/// </summary>
	private Node SceneRoot => GetParent() ?? GetTree().Root;

	/// <summary>Current round number. Starts at 1 and advances when the rotation wraps around.</summary>
	public int CurrentTurn { get; private set; } = 1;

	/// <summary>True while the player may aim / charge / select nodes / fire.</summary>
	public bool IsPlayerTurn { get; private set; } = true;

	/// <summary>
	/// True while a turn's events are still resolving (control is locked).
	/// A turn is committed the moment its first TurnEvent is registered.
	/// </summary>
	public bool IsResolvingTurn { get; private set; } = false;

	private Vector3 _cameraOffset;
	private float _currentAimAngle = 0f;
	private MeshInstance3D _aimIndicator;
	private float _currentPowerPercent = 0f;
	private bool _isCharging = false;
	private bool _isChargingUp = true;
	private AmmoType _currentAmmo = AmmoType.Node;

	// In-flight camera centering tween, killed the moment the player pans.
	private Tween _cameraTween;

	// Minimap widgets (created at runtime in CreateMinimap).
	private SubViewport _minimapViewport;
	private Camera3D _minimapCamera;
	private TextureRect _minimapRect;
	private ColorRect _minimapFrame;
	private ColorRect _minimapViewIndicator;
	private Vector2I _lastMinimapPixelSize = Vector2I.Zero;

	// Node markers on the minimap (BaseNode -> dot), reconciled every frame.
	private readonly Dictionary<BaseNode, ColorRect> _minimapMarkers = new Dictionary<BaseNode, ColorRect>();

	// Connection lines on the minimap (child node -> line to its parent),
	// reconciled every frame. Drawn under the markers via a dedicated layer.
	private readonly Dictionary<BaseNode, Line2D> _minimapLines = new Dictionary<BaseNode, Line2D>();
	private Node2D _minimapLineLayer;

	// True while the OS cursor is inside the game window. Edge panning is
	// disabled otherwise so a stale edge position never keeps panning.
	private bool _mouseInsideWindow = true;

	// Every BaseNode currently in the game, for locating surviving chain roots.
	private readonly List<BaseNode> _allNodes = new List<BaseNode>();

	// TurnEvents that must finish before the current turn's resolution completes.
	private readonly List<TurnEvent> _pendingTurnEvents = new List<TurnEvent>();

	// The combatants taking turns, in rotation order, and the slot acting now.
	private readonly List<Combatant> _combatants = new List<Combatant>();
	private int _activeIndex = -1;
	private PlayerCombatant _playerCombatant;
	private EnemyCombatant _enemyCombatant;

	// Guards against queueing the scene reload twice.
	private bool _restartRequested = false;

	public override void _Ready()
	{
		Instance = this;
		
		// 1. Setup Camera Offset
		if (MainCamera != null)
		{
			var startBase = SceneRoot.FindChild("BaseNode", true, false) as Node3D;
			if (startBase != null)
				_cameraOffset = MainCamera.GlobalPosition - startBase.GlobalPosition;
			else
				_cameraOffset = new Vector3(0, 15, 15); // Standard top-down angle
		}

		CreateAimIndicator();
		CreateMinimap();
		UpdateSelectedAmmo();
		if (PowerBar != null) PowerBar.Visible = false;
		if (DefeatLabel != null) DefeatLabel.Visible = false;
		if (VictoryLabel != null) VictoryLabel.Visible = false;
		if (RestartLabel != null) RestartLabel.Visible = false;
		UpdateTurnLabel();

		// Track whether the OS cursor is inside the game window so edge
		// panning never triggers from a stale position after it leaves.
		Window window = GetWindow();
		window.MouseEntered += () => _mouseInsideWindow = true;
		window.MouseExited += () => _mouseInsideWindow = false;
		window.SizeChanged += UpdateMinimapLayout; // Keep the minimap sized to the window.

		// Build the match and hand the first turn to the first combatant.
		NodeContainer = new Node3D { Name = "Nodes" };
		AddChild(NodeContainer);
		CreateCombatants();

		// The first turn starts once the whole scene has finished setting up: the
		// starting node's _Ready (which builds its highlight ring and health bar)
		// has not run yet at this point, so it could not be selected.
		GetTree().CreateTimer(0.0f).Timeout += StartFirstTurn;
	}

	// ------------------------------------------------------------------
	// Combatants
	// ------------------------------------------------------------------

	/// <summary>
	/// Builds the match: the player's chain (the BaseNode already placed in the
	/// scene) plus one enemy chain spawned <see cref="EnemyStartScreensRight"/>
	/// screens to the right of it.
	///
	/// This is the single place opponents are declared. Adding a second enemy —
	/// or a totally different kind of combatant, since the rotation only ever
	/// talks to <see cref="Combatant"/> — means creating it here and adding it
	/// to _combatants, on a team other than the player's.
	/// </summary>
	private void CreateCombatants()
	{
		// --- Player (team 0) ---
		_playerCombatant = new PlayerCombatant
		{
			Name = "PlayerCombatant",
			DisplayName = "You",
			Team = 0,
			MinimapColor = MinimapNodeColor,
		};
		AddChild(_playerCombatant);
		_combatants.Add(_playerCombatant);

		// Looked up inside this scene (never the tree root) so a node left
		// outside it — anything that outlived an earlier match — cannot be
		// adopted as the player's root.
		var startBase = SceneRoot.FindChild("BaseNode", true, false) as BaseNode;
		if (startBase != null)
		{
			// Adopted before the node's _Ready runs (GameManager is ready first, so
			// its _Ready can register the node with this chain).
			_playerCombatant.AttachRoot(startBase);
		}
		else
		{
			GD.PushWarning("No starting BaseNode found in the scene — spawning the player's root at the origin.");
			_playerCombatant.SpawnRoot(Vector3.Zero);
		}

		// --- Enemy (team 1) ---
		_enemyCombatant = new EnemyCombatant
		{
			Name = "EnemyCombatant",
			DisplayName = "Enemy",
			Team = 1,
			MinimapColor = MinimapEnemyNodeColor,
			ThinkTime = EnemyThinkTime,
			AimSpreadDegrees = EnemyAimSpreadDegrees,
			MinPowerPercent = EnemyMinPower,
			MaxPowerPercent = EnemyMaxPower,
		};
		AddChild(_enemyCombatant);
		_combatants.Add(_enemyCombatant);
		BaseNode enemyRoot = _enemyCombatant.SpawnRoot(GetEnemyStartPosition());

		GD.Print($"[Match] {_combatants.Count} combatants ready. Enemy root at {enemyRoot?.GlobalPosition} "
			+ $"({EnemyStartScreensRight} screens right of the player's root).");
	}

	/// <summary>
	/// The enemy's starting spot: <see cref="EnemyStartScreensRight"/> screen
	/// widths to the right of the player's root, on the same Z.
	/// </summary>
	private Vector3 GetEnemyStartPosition()
	{
		Vector3 origin = Vector3.Zero;
		if (_playerCombatant?.RootNode != null && IsInstanceValid(_playerCombatant.RootNode))
		{
			origin = _playerCombatant.RootNode.GlobalPosition;
		}

		float screenWidth = (MainCamera != null ? MainCamera.Size : 30f) * GetViewportAspect();
		return new Vector3(origin.X + screenWidth * EnemyStartScreensRight, 0f, origin.Z);
	}

	private void UpdateSelectedAmmo()
	{
		if (SelectorBox == null) return;

		// Determine which icon to highlight
		TextureRect targetIcon = (_currentAmmo == AmmoType.Node) ? NodeIcon : BombIcon;

		// Smoothly move the selector box to the icon's position
		Tween tween = GetTree().CreateTween();
		tween.SetTrans(Tween.TransitionType.Back);
		tween.SetEase(Tween.EaseType.Out);
		
		// We target the global_position of the icon
		tween.TweenProperty(SelectorBox, "global_position", targetIcon.GlobalPosition, 0.2f);
	}

	private void CreateAimIndicator()
	{
		_aimIndicator = new MeshInstance3D();
		
		// Create a long thin box that looks like a pointer
		var mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 2.0f) };
		_aimIndicator.Mesh = mesh;
		
		// Bright yellow material so it stands out
		var mat = new StandardMaterial3D { 
			AlbedoColor = new Color(1, 1, 0), 
			ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded 
		};
		_aimIndicator.MaterialOverride = mat;
		
		_aimIndicator.Visible = false;
		AddChild(_aimIndicator);
	}

	public void SelectNode(BaseNode node)
	{
		// No selecting while the turn's events are still resolving.
		if (!IsPlayerTurn) return;
		if (node == null || !IsInstanceValid(node) || node.IsDestroyed) return;
		// Only the player's own chain is controllable — enemy nodes are never
		// selectable, so a click can't hijack the opponent's chain.
		if (node.OwnerChain != _playerCombatant) return;

		// The previous selection may have been destroyed; only touch it
		// while it is still a valid instance.
		if (SelectedNode != null && IsInstanceValid(SelectedNode))
		{
			SelectedNode.SetHighlight(false);
		}
		SelectedNode = node;
		SelectedNode.SetHighlight(true);
		
		// Show indicator and move camera
		_aimIndicator.Visible = true;
		CenterCameraOn(node.GlobalPosition);

		// Cancel charging if we switch nodes
		_isCharging = false;
		_currentPowerPercent = 0f;
		if (PowerBar != null) PowerBar.Visible = false;
	}

	public override void _Process(double delta)
	{
		HandleCameraPanning((float)delta);
		UpdateMinimapViewIndicator();
		UpdateMinimapMarkers();
		UpdateMinimapLines();

		// Safety net: never keep control pointed at a destroyed node.
		// If the selected node dies, control reverts to the highest
		// remaining node in the chain (or ends the game in defeat if none
		// remain).
		if (SelectedNode != null && (!IsInstanceValid(SelectedNode) || SelectedNode.IsDestroyed))
		{
			if (!IsResolvingTurn)
			{
				RevertControlToHighestNode();
			}
			// During resolution the selection is re-established in CompleteTurn().
			return;
		}

		if (!IsPlayerTurn || SelectedNode == null) return;

		HandleAiming((float)delta);
		HandleFiringLogic((float)delta);
	}

	private void HandleAiming(float delta)
	{
		float input = Input.GetAxis("aim_left", "aim_right");
		_currentAimAngle += input * RotationSpeed * delta;

		// Switch Ammo Type with Q or E
		if (Input.IsActionJustPressed("switch_ammo_next") || Input.IsActionJustPressed("switch_ammo_previous"))
		{
			_currentAmmo = _currentAmmo == AmmoType.Node ? AmmoType.Bomb : AmmoType.Node;
			GD.Print("Switched to: " + _currentAmmo);
			UpdateSelectedAmmo();
		}

		ShowAimPreview(SelectedNode, _currentAimAngle, _currentPowerPercent);
	}

	/// <summary>
	/// Puts the shared aim arrow on <paramref name="origin"/> at the given aim
	/// angle and power level. The player's aiming and every AI combatant's
	/// aiming both go through here, so all shots are previewed identically.
	/// </summary>
	/// <param name="origin">Node the shot would be launched from.</param>
	/// <param name="aimAngleRadians">Aim angle, in the aim-indicator convention.</param>
	/// <param name="powerPercent">Charge level, 0..1.</param>
	/// <param name="showPowerMeter">Also drive the shared power bar (used during an AI turn).</param>
	public void ShowAimPreview(BaseNode origin, float aimAngleRadians, float powerPercent, bool showPowerMeter = false)
	{
		if (_aimIndicator == null) return;
		if (origin == null || !IsInstanceValid(origin) || origin.IsDestroyed) return;

		float power = Mathf.Clamp(powerPercent, 0f, 1f);

		_aimIndicator.Visible = true;
		_aimIndicator.Scale = new Vector3(1, 1, 1 + (power * 2f));
		_aimIndicator.GlobalPosition = origin.GlobalPosition + Vector3.Up * 1.0f;
		_aimIndicator.Rotation = new Vector3(0, aimAngleRadians, 0);
		_aimIndicator.GlobalPosition += _aimIndicator.GlobalTransform.Basis.Z * 1.5f;

		if (showPowerMeter && PowerBar != null)
		{
			PowerBar.Visible = true;
			PowerBar.Value = power;
		}
	}

	private void HideAimIndicator()
	{
		if (_aimIndicator != null) _aimIndicator.Visible = false;
	}

	private void HandleFiringLogic(float delta)
	{
		// 1. Start Charging
		if (Input.IsActionJustPressed("fire_shot"))
		{
			_isCharging = true;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = true;
		}

		// 2. While Holding
		if (_isCharging && Input.IsActionPressed("fire_shot"))
		{
			var effectiveChargeSpeed = _isChargingUp ? ChargeSpeed : -1f * ChargeSpeed;
			_currentPowerPercent += effectiveChargeSpeed * delta;
			_currentPowerPercent = Mathf.Clamp(_currentPowerPercent, 0f, 1f);
			if (_currentPowerPercent == 1f)
			{
				_isChargingUp = false;
			}
			else if (_currentPowerPercent == 0f)
			{
				_isChargingUp = true;
			}
			
			if (PowerBar != null) PowerBar.Value = _currentPowerPercent;

			// Optional: Scale the indicator arrow to show power visually in 3D
			_aimIndicator.Scale = new Vector3(1, 1, 1 + (_currentPowerPercent * 2f));
		}

		// 3. Release and Fire
		if (_isCharging && Input.IsActionJustReleased("fire_shot"))
		{
			_isChargingUp = true;
			Fire(_currentPowerPercent);
			
			// Reset
			_isCharging = false;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = false;
			_aimIndicator.Scale = Vector3.One;
		}
	}

	/// <summary>
	/// Fires the currently selected ammo along the player's current aim. Uses
	/// the exact same launch path as every AI combatant.
	/// </summary>
	/// <param name="powerPercent">Charge level, 0..1, mapped onto the launch force range.</param>
	private void Fire(float powerPercent)
	{
		AmmoLauncher.Launch(_currentAmmo, SelectedNode, _currentAimAngle, powerPercent, null);
	}

	// ------------------------------------------------------------------
	// Turn system
	// ------------------------------------------------------------------

	/// <summary>
	/// Registers an event that must resolve before the current turn ends.
	/// Registering the first event commits the turn and takes control away
	/// from the player; control returns when the last pending event resolves.
	/// </summary>
	public void RegisterTurnEvent(TurnEvent evt)
	{
		if (evt == null || evt.IsResolved) return;

		if (!IsResolvingTurn)
		{
			// Committing the turn: lock player control immediately.
			IsResolvingTurn = true;
			IsPlayerTurn = false;

			// Cancel any in-flight charging state.
			_isCharging = false;
			_currentPowerPercent = 0f;
			if (PowerBar != null) PowerBar.Visible = false;
			if (_aimIndicator != null) _aimIndicator.Visible = false;

			GD.Print($"[Turn {CurrentTurn}] Turn committed — control locked.");
		}

		_pendingTurnEvents.Add(evt);
		evt.Resolved += () => OnTurnEventResolved(evt);
		GD.Print($"[Turn {CurrentTurn}] Event started: {evt.Description}");
	}

	private void OnTurnEventResolved(TurnEvent evt)
	{
		if (!_pendingTurnEvents.Remove(evt)) return;
		GD.Print($"[Turn {CurrentTurn}] Event resolved: {evt.Description}");

		if (_pendingTurnEvents.Count == 0 && IsResolvingTurn)
		{
			CompleteTurn();
		}
	}

	/// <summary>
	/// Every pending event has finished: the acting combatant's turn is over.
	/// The match is checked for a winner, and otherwise the turn passes to the
	/// next combatant that can still act.
	/// </summary>
	private void CompleteTurn()
	{
		IsResolvingTurn = false;
		ActiveCombatant?.EndTurn();

		GD.Print($"[Turn {CurrentTurn}] Resolution complete.");

		// A chain whose root was destroyed cascades through every child, so a
		// combatant with no root left loses the match for its team.
		if (EvaluateGameOver()) return;

		AdvanceTurn();
	}

	/// <summary>
	/// Ends the active turn for a combatant that spent its whole turn without
	/// registering a single TurnEvent (for example a shot that could not be
	/// launched). Without this the rotation would wait forever for events that
	/// are never coming. Does nothing while events are still resolving.
	/// </summary>
	public void EndIdleTurn()
	{
		if (IsResolvingTurn || IsGameOver) return;
		CompleteTurn();
	}

	/// <summary>
	/// Hands the first turn of the match to the first combatant in the rotation.
	/// </summary>
	private void StartFirstTurn()
	{
		if (_combatants.Count == 0) return;

		_activeIndex = 0;
		CurrentTurn = 1;
		StartTurn(_combatants[_activeIndex]);
	}

	/// <summary>
	/// Begins <paramref name="combatant"/>'s turn: a player-controlled combatant
	/// gets control back (on its last selected node when that node survived, or
	/// on the top of its chain otherwise), an AI combatant starts acting.
	/// </summary>
	private void StartTurn(Combatant combatant)
	{
		ActiveCombatant = combatant;
		if (combatant == null) return;

		if (combatant.IsPlayerControlled)
		{
			IsPlayerTurn = true;

			// Note: a destroyed node may still be a "valid" instance at this
			// point (QueueFree removes it at the end of the frame), so check
			// the IsDestroyed flag rather than IsInstanceValid alone.
			bool selectionSurvived = SelectedNode != null
				&& IsInstanceValid(SelectedNode)
				&& !SelectedNode.IsDestroyed
				&& SelectedNode.OwnerChain == combatant;

			BaseNode target = selectionSurvived ? SelectedNode : _playerCombatant.FindHighestRemainingNode();
			SelectNode(target);
			GD.Print($"[Turn {CurrentTurn}] {combatant.DisplayName}: move.");
		}
		else
		{
			// No input for this combatant — it acts on its own from here.
			IsPlayerTurn = false;
			HideAimIndicator();

			// An enemy may be well off-screen (it starts two screens away), so
			// follow whoever is acting; the player's own turn centers the camera
			// back on their selection through SelectNode().
			if (CenterCameraOnActingCombatant
				&& combatant is NodeChainCombatant chain
				&& chain.ActionNode != null)
			{
				CenterCameraOn(chain.ActionNode.GlobalPosition);
			}

			GD.Print($"[Turn {CurrentTurn}] {combatant.DisplayName}: move.");
			combatant.BeginTurn();
		}

		UpdateTurnLabel();
	}

	/// <summary>
	/// Passes the turn to the next combatant that can still act, skipping
	/// defeated ones, and counts a round each time the rotation wraps around.
	/// </summary>
	private void AdvanceTurn()
	{
		int count = _combatants.Count;
		if (count == 0) return;

		bool roundCounted = false;
		for (int step = 1; step <= count; step++)
		{
			int next = (_activeIndex + step) % count;

			// Wrapped past the end of the rotation: one full round has passed.
			if (next <= _activeIndex && !roundCounted)
			{
				CurrentTurn++;
				roundCounted = true;
			}

			if (_combatants[next].IsDefeated) continue;

			_activeIndex = next;
			StartTurn(_combatants[next]);
			return;
		}

		// Every combatant is defeated; EvaluateGameOver() should have ended the
		// match before this could happen.
	}

	/// <summary>
	/// Ends the match once a whole team has no combatant left that can act:
	/// defeat when the player's team is gone, victory when every enemy is
	/// (destroying the enemy chain's root takes its whole chain with it).
	/// Returns true when the match ended.
	/// </summary>
	private bool EvaluateGameOver()
	{
		if (_playerCombatant == null || _combatants.Count == 0) return false;

		int playerTeam = _playerCombatant.Team;
		bool playerAlive = false;
		bool enemiesAlive = false;

		foreach (Combatant combatant in _combatants)
		{
			if (combatant.IsDefeated) continue;
			if (combatant.Team == playerTeam) playerAlive = true;
			else enemiesAlive = true;
		}

		if (playerAlive && enemiesAlive) return false;

		EndGame(playerAlive && !enemiesAlive);
		return true;
	}

	/// <summary>
	/// Shows the outcome overlay and stops the rotation. Any key or click from
	/// here on restarts the match (see <see cref="_UnhandledInput"/>).
	/// </summary>
	/// <param name="playerWon">True for the victory screen, false for defeat.</param>
	private void EndGame(bool playerWon)
	{
		IsGameOver = true;
		IsPlayerTurn = false;
		IsResolvingTurn = false;
		SelectedNode = null;
		HideAimIndicator();
		if (PowerBar != null) PowerBar.Visible = false;
		if (RestartLabel != null) RestartLabel.Visible = true;

		if (playerWon)
		{
			if (VictoryLabel != null) VictoryLabel.Visible = true;
			GD.Print("VICTORY: every enemy chain has been destroyed.");
		}
		else
		{
			if (DefeatLabel != null) DefeatLabel.Visible = true;
			GD.Print("DEFEAT: no nodes remain in the chain.");
		}

		UpdateTurnLabel();
	}

	/// <summary>
	/// Any key press or click after the match has ended restarts it by reloading
	/// the scene. The reload is deferred by a frame so it never runs from inside
	/// a node that the reload is about to free.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsGameOver || _restartRequested) return;

		bool pressed = (@event is InputEventKey key && key.Pressed && !key.Echo)
			|| (@event is InputEventMouseButton mouseButton && mouseButton.Pressed);
		if (!pressed) return;

		_restartRequested = true;
		GD.Print("Restarting the match.");
		GetTree().CreateTimer(0.0f).Timeout += () => GetTree().ReloadCurrentScene();
	}

	/// <summary>
	/// Hands control back to the topmost surviving node of the player's chain.
	/// Called after destruction so control never gets stuck on a dead node.
	/// </summary>
	private void RevertControlToHighestNode()
	{
		BaseNode highest = _playerCombatant?.FindHighestRemainingNode();
		if (highest == null)
		{
			EndGame(false);
			return;
		}
		SelectNode(highest);
	}

	// ------------------------------------------------------------------
	// Node registry
	// ------------------------------------------------------------------

	public void RegisterNode(BaseNode node)
	{
		if (node != null && !_allNodes.Contains(node))
		{
			_allNodes.Add(node);
		}
	}

	public void UnregisterNode(BaseNode node)
	{
		_allNodes.Remove(node);
	}

	/// <summary>Minimap color for a node: the selection stands out, otherwise nodes use their owner's color.</summary>
	private Color GetNodeMinimapColor(BaseNode node)
	{
		if (node == SelectedNode) return MinimapSelectedNodeColor;
		if (node.OwnerChain != null && IsInstanceValid(node.OwnerChain)) return node.OwnerChain.MinimapColor;
		return MinimapNodeColor;
	}

	/// <summary>Shows the round number and whose move it is.</summary>
	private void UpdateTurnLabel()
	{
		if (TurnLabel == null) return;

		if (IsGameOver)
		{
			TurnLabel.Text = "Game over";
			return;
		}

		if (ActiveCombatant == null)
		{
			TurnLabel.Text = $"Turn {CurrentTurn}";
			return;
		}

		string whose = ActiveCombatant.IsPlayerControlled ? "your move" : $"{ActiveCombatant.DisplayName}'s move";
		TurnLabel.Text = $"Turn {CurrentTurn} — {whose}";
	}

	private void CenterCameraOn(Vector3 targetPosition)
	{
		if (MainCamera == null) return;

		// Stop any in-flight panning/centering tween first.
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}

		// Calculate destination based on ground position to keep height consistent
		Vector3 groundLevel = new Vector3(targetPosition.X, 0, targetPosition.Z);
		Vector3 destination = groundLevel + _cameraOffset;

		_cameraTween = GetTree().CreateTween();
		_cameraTween.SetTrans(Tween.TransitionType.Expo); 
		_cameraTween.SetEase(Tween.EaseType.Out);
		
		_cameraTween.TweenProperty(MainCamera, "global_position", destination, CameraSmoothTime);
		
		// Optional: Ensure camera doesn't accidentally rotate
		_cameraTween.Parallel().TweenProperty(MainCamera, "global_rotation", MainCamera.GlobalRotation, CameraSmoothTime);
	}

	// ------------------------------------------------------------------
	// Camera: minimap & screen-edge panning
	// ------------------------------------------------------------------

	/// <summary>
	/// Builds the RTS-style minimap: a SubViewport with a top-down camera
	/// rendered into a TextureRect in the bottom-right corner, plus a
	/// translucent rectangle showing the main camera's current view.
	/// Clicking the minimap centers the main camera on that spot.
	///
	/// The minimap currently shows a fixed-size subset of the (effectively
	/// infinite) ground plane — see GetMinimapWorldRect() for the single
	/// seam to swap in a real map-sized region later.
	/// </summary>
	private void CreateMinimap()
	{
		if (MainCamera == null) return;
		var uiLayer = GetNodeOrNull<CanvasLayer>("../UI");
		if (uiLayer == null) return;

		MinimapWorldSize = Mathf.Max(MinimapWorldSize, 1f);

		// 1. Off-screen SubViewport that re-renders the shared 3D world top-down.
		// Its resolution is matched to the on-screen widget by UpdateMinimapLayout().
		_minimapViewport = new SubViewport
		{
			Name = "MinimapViewport",
			Size = new Vector2I(256, 256),
			TransparentBg = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			GuiDisableInput = true,
			PhysicsObjectPicking = false,
			OwnWorld3D = false, // Render the same world the player sees.
		};

		_minimapCamera = new Camera3D
		{
			Name = "MinimapCamera",
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = MinimapWorldSize,
			Position = new Vector3(MinimapCenter.X, 2000, MinimapCenter.Y),
			RotationDegrees = new Vector3(-90, 0, 0),
			Near = 1.0f,
			Far = 4000.0f,
		};
		_minimapViewport.AddChild(_minimapCamera);
		_minimapCamera.Current = true; // Make it the viewport's active camera.
		AddChild(_minimapViewport);

		// 2. A thin frame behind the minimap so it stands out on the ground.
		_minimapFrame = new ColorRect
		{
			Name = "MinimapFrame",
			Color = new Color(0, 0, 0, 0.6f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_minimapFrame.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		uiLayer.AddChild(_minimapFrame);

		// 3. The on-screen minimap widget (bottom-right corner).
		_minimapRect = new TextureRect
		{
			Name = "Minimap",
			Texture = _minimapViewport.GetTexture(),
			ClipContents = true,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_minimapRect.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		uiLayer.AddChild(_minimapRect);

		// 4. Translucent rectangle showing where the main camera is looking.
		_minimapViewIndicator = new ColorRect
		{
			Name = "ViewIndicator",
			Color = MinimapViewRectColor,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_minimapRect.AddChild(_minimapViewIndicator);

		// 5. Layer for parent->child connection lines. Added before any
		// marker dots exist, so lines always draw underneath the dots.
		_minimapLineLayer = new Node2D { Name = "ConnectionLines" };
		_minimapRect.AddChild(_minimapLineLayer);

		// 6. Clicking the minimap moves the camera to that spot.
		_minimapRect.GuiInput += OnMinimapGuiInput;

		// Size the widget (and viewport) for the current window.
		UpdateMinimapLayout();
	}

	/// <summary>
	/// Sizes the minimap to the game window: its side is
	/// MinimapScreenFraction of the window's shorter side. The SubViewport
	/// resolution is matched to the widget so the render stays crisp, and
	/// the frame hugs the widget. Runs on window resize; every minimap
	/// mapping reads _minimapRect.Size, so this is the only place that
	/// needs to know the pixel layout.
	/// </summary>
	private void UpdateMinimapLayout()
	{
		if (_minimapRect == null || _minimapViewport == null || _minimapFrame == null) return;

		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		float side = Mathf.Max(1f, Mathf.Min(viewportSize.X, viewportSize.Y) * MinimapScreenFraction);
		Vector2I pixelSize = new Vector2I(Mathf.RoundToInt(side), Mathf.RoundToInt(side));
		if (pixelSize == _lastMinimapPixelSize) return;
		_lastMinimapPixelSize = pixelSize;

		_minimapViewport.Size = pixelSize;

		_minimapRect.OffsetLeft = -pixelSize.X - 8;
		_minimapRect.OffsetTop = -pixelSize.Y - 8;
		_minimapRect.OffsetRight = -8;
		_minimapRect.OffsetBottom = -8;

		_minimapFrame.OffsetLeft = -pixelSize.X - 10;
		_minimapFrame.OffsetTop = -pixelSize.Y - 10;
		_minimapFrame.OffsetRight = -6;
		_minimapFrame.OffsetBottom = -6;
	}

	private void OnMinimapGuiInput(InputEvent @event)
	{
		if (_minimapRect == null) return;
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			Vector2 local = _minimapRect.GetLocalMousePosition();
			Vector2 size = _minimapRect.Size;
			if (size.X <= 0f || size.Y <= 0f) return;

			// Map the click to a world position on the ground plane.
			float nx = Mathf.Clamp(local.X / size.X, 0f, 1f);
			float ny = Mathf.Clamp(local.Y / size.Y, 0f, 1f);
			Rect2 worldRect = GetMinimapWorldRect();
			Vector3 target = new Vector3(
				worldRect.Position.X + nx * worldRect.Size.X,
				0f,
				worldRect.Position.Y + ny * worldRect.Size.Y);
			CenterCameraOn(target);
		}
	}

	/// <summary>
	/// The world-space XZ rectangle the minimap displays.
	///
	/// Currently a fixed-size subset of the (effectively infinite) ground
	/// plane: MinimapWorldSize wide/tall centered on MinimapCenter. When a
	/// real map with defined bounds exists, replace the body of this method
	/// with a rect derived from the map size (e.g. from PanBoundsMin/Max) so
	/// the minimap automatically scales to the whole map. The top-down camera
	/// size in CreateMinimap() must then follow the same rect.
	/// </summary>
	private Rect2 GetMinimapWorldRect()
	{
		float half = MinimapWorldSize * 0.5f;
		return new Rect2(MinimapCenter.X - half, MinimapCenter.Y - half, MinimapWorldSize, MinimapWorldSize);
	}

	/// <summary>Converts a world-space XZ position to minimap pixel coordinates.</summary>
	private Vector2 WorldToMinimap(Vector2 worldXZ)
	{
		if (_minimapRect == null) return Vector2.Zero;
		Rect2 worldRect = GetMinimapWorldRect();
		Vector2 normalized = new Vector2(
			(worldXZ.X - worldRect.Position.X) / worldRect.Size.X,
			(worldXZ.Y - worldRect.Position.Y) / worldRect.Size.Y);
		return new Vector2(normalized.X * _minimapRect.Size.X, normalized.Y * _minimapRect.Size.Y);
	}

	/// <summary>
	/// Positions and sizes the minimap's view indicator to match the main
	/// camera's ground footprint.
	/// </summary>
	private void UpdateMinimapViewIndicator()
	{
		if (_minimapRect == null || _minimapViewIndicator == null || MainCamera == null) return;

		// Ray from the main camera through the screen center down to the ground.
		Vector3 camPos = MainCamera.GlobalPosition;
		Vector3 forward = -MainCamera.GlobalTransform.Basis.Z;
		if (Mathf.Abs(forward.Y) < 0.001f) return;
		float t = -camPos.Y / forward.Y;
		if (t < 0f) return; // Camera not looking at the ground plane.
		Vector3 groundLookAt = camPos + forward * t;

		// Project the camera's screen-space footprint onto the ground plane.
		Vector3 up = MainCamera.GlobalTransform.Basis.Y;
		Vector3 right = MainCamera.GlobalTransform.Basis.X;
		Vector2 upGround = new Vector2(up.X, up.Z);
		Vector2 rightGround = new Vector2(right.X, right.Z);
		if (upGround.LengthSquared() < 0.001f || rightGround.LengthSquared() < 0.001f) return;

		float aspect = GetViewportAspect();
		float halfHeightWorld = (MainCamera.Size * 0.5f) / upGround.Length();
		float halfWidthWorld = (MainCamera.Size * 0.5f * aspect) / rightGround.Length();

		Vector2 center = WorldToMinimap(new Vector2(groundLookAt.X, groundLookAt.Z));
		Rect2 worldRect = GetMinimapWorldRect();
		Vector2 halfMinimap = new Vector2(
			halfWidthWorld / worldRect.Size.X * _minimapRect.Size.X,
			halfHeightWorld / worldRect.Size.Y * _minimapRect.Size.Y);

		_minimapViewIndicator.Position = center - halfMinimap;
		_minimapViewIndicator.Size = halfMinimap * 2f;
	}

	private float GetViewportAspect()
	{
		Vector2 size = GetViewport().GetVisibleRect().Size;
		return size.Y > 0f ? size.X / size.Y : 1f;
	}

	/// <summary>
	/// Reconciles the minimap's node markers: creates a dot for every live
	/// node (black) and highlights the selected node (yellow), and removes
	/// markers for nodes that have been destroyed. Dots are plain ColorRects
	/// clipped to the minimap, so anything outside the shown region vanishes.
	/// </summary>
	private void UpdateMinimapMarkers()
	{
		if (_minimapRect == null) return;

		foreach (BaseNode node in _allNodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;

			if (!_minimapMarkers.TryGetValue(node, out ColorRect marker) || !IsInstanceValid(marker))
			{
				marker = new ColorRect
				{
					Size = MinimapMarkerSize,
					Color = MinimapNodeColor,
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
				_minimapRect.AddChild(marker);
				_minimapMarkers[node] = marker;
			}

			marker.Color = GetNodeMinimapColor(node);
			Vector2 pos = WorldToMinimap(new Vector2(node.GlobalPosition.X, node.GlobalPosition.Z));
			marker.Position = pos - marker.Size * 0.5f;
		}

		// Drop markers whose node has been destroyed, freed, or left the
		// registry since last frame.
		List<BaseNode> dead = null;
		foreach (KeyValuePair<BaseNode, ColorRect> pair in _minimapMarkers)
		{
			if (!IsInstanceValid(pair.Key) || pair.Key.IsDestroyed || !_allNodes.Contains(pair.Key))
			{
				pair.Value.QueueFree();
				if (dead == null) dead = new List<BaseNode>();
				dead.Add(pair.Key);
			}
		}
		if (dead != null)
		{
			foreach (BaseNode key in dead)
			{
				_minimapMarkers.Remove(key);
			}
		}
	}

	/// <summary>
	/// Reconciles the minimap's connection lines: a plain line for every
	/// child node with a live parent, drawn between the two dots (no
	/// directionality). Lines live in their own layer under the markers and
	/// are clipped to the minimap like everything else.
	/// </summary>
	private void UpdateMinimapLines()
	{
		if (_minimapLineLayer == null) return;

		foreach (BaseNode node in _allNodes)
		{
			if (node == null || !IsInstanceValid(node) || node.IsDestroyed) continue;
			BaseNode parent = node.ParentBase;
			if (parent == null || !IsInstanceValid(parent) || parent.IsDestroyed) continue;

			if (!_minimapLines.TryGetValue(node, out Line2D line) || !IsInstanceValid(line))
			{
				line = new Line2D
				{
					Width = MinimapLineWidth,
					DefaultColor = MinimapLineColor,
				};
				_minimapLineLayer.AddChild(line);
				_minimapLines[node] = line;
			}

			line.Points = new[]
			{
				WorldToMinimap(new Vector2(parent.GlobalPosition.X, parent.GlobalPosition.Z)),
				WorldToMinimap(new Vector2(node.GlobalPosition.X, node.GlobalPosition.Z)),
			};
		}

		// Drop lines whose node (or its parent) has died or left the chain.
		List<BaseNode> dead = null;
		foreach (KeyValuePair<BaseNode, Line2D> pair in _minimapLines)
		{
			BaseNode node = pair.Key;
			BaseNode parent = node?.ParentBase;
			if (!IsInstanceValid(node) || node.IsDestroyed || !_allNodes.Contains(node)
				|| parent == null || !IsInstanceValid(parent) || parent.IsDestroyed)
			{
				pair.Value.QueueFree();
				if (dead == null) dead = new List<BaseNode>();
				dead.Add(node);
			}
		}
		if (dead != null)
		{
			foreach (BaseNode key in dead)
			{
				_minimapLines.Remove(key);
			}
		}
	}

	/// <summary>
	/// Pans the camera when the mouse cursor is near a screen edge, in
	/// classic RTS style. Speed ramps up the deeper the cursor sits inside
	/// the edge margin. Never fights a centering tween — panning wins.
	/// </summary>
	private void HandleCameraPanning(float delta)
	{
		if (MainCamera == null) return;
		// Never pan from a stale position: the cursor must actually be inside
		// the game window and the window focused.
		if (!_mouseInsideWindow) return;
		if (!GetViewport().GetWindow().HasFocus()) return;
		// Don't pan while the cursor is over UI (minimap, ammo bar, labels).
		if (GetViewport().GuiGetHoveredControl() != null) return;

		Vector2 mouse = GetViewport().GetMousePosition();
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f) return;

		float margin = PanEdgeMargin;
		Vector2 panDir = Vector2.Zero;
		float edgeDepth = 0f;

		if (mouse.X <= margin)
		{
			panDir.X = -1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((margin - mouse.X) / margin, 0f, 1f));
		}
		else if (mouse.X >= viewportSize.X - margin)
		{
			panDir.X = 1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((mouse.X - (viewportSize.X - margin)) / margin, 0f, 1f));
		}

		if (mouse.Y <= margin)
		{
			panDir.Y = -1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((margin - mouse.Y) / margin, 0f, 1f));
		}
		else if (mouse.Y >= viewportSize.Y - margin)
		{
			panDir.Y = 1f;
			edgeDepth = Mathf.Max(edgeDepth, Mathf.Clamp((mouse.Y - (viewportSize.Y - margin)) / margin, 0f, 1f));
		}

		if (panDir == Vector2.Zero) return;

		// Take over from any in-flight centering tween.
		if (_cameraTween != null && _cameraTween.IsValid())
		{
			_cameraTween.Kill();
		}

		// Convert the screen-space pan direction to a ground-plane direction.
		Vector3 move = MainCamera.GlobalTransform.Basis.X * panDir.X
					 + MainCamera.GlobalTransform.Basis.Y * (-panDir.Y);
		move.Y = 0f;
		if (move.LengthSquared() < 0.0001f) return;
		move = move.Normalized();

		Vector3 newPos = MainCamera.GlobalPosition + move * (PanSpeed * edgeDepth * delta);
		newPos.X = Mathf.Clamp(newPos.X, Mathf.Min(PanBoundsMin.X, PanBoundsMax.X), Mathf.Max(PanBoundsMin.X, PanBoundsMax.X));
		newPos.Z = Mathf.Clamp(newPos.Z, Mathf.Min(PanBoundsMin.Y, PanBoundsMax.Y), Mathf.Max(PanBoundsMin.Y, PanBoundsMax.Y));
		MainCamera.GlobalPosition = newPos;
	}
}

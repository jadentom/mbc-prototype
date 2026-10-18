using Godot;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MbcPrototype.Combat;
using MbcPrototype.Enemies;
using MbcPrototype.Presentation;
using MbcPrototype.Rules;
using MbcPrototype.Simulation;
using MbcPrototype.Testing;
using MbcPrototype.TurnSystem;
using MbcPrototype.Units;
using MbcPrototype.World;

namespace MbcPrototype.Core;

public partial class GameManager : Node
{
	public static GameManager Instance;

	[ExportGroup("References")]
	[Export] public Camera3D MainCamera;
	[Export] public ProgressBar PowerBar;

	[ExportGroup("Presentation")]
	/// <summary>Center the camera on whichever combatant is acting, so a move somewhere else on the map is visible.</summary>
	[Export] public bool CenterCameraOnActingCombatant = true;

	/// <summary>
	/// Build the developer overlay (F1): the live tuning surface from docs/parity
	/// M0.7. It is the only lever that changes balance without a rebuild, so it is
	/// on by default; turn it off for a screenshot or a recording.
	/// </summary>
	[Export] public bool EnableDevOverlay = true;

	[ExportGroup("UI References")]
	[Export] public Label TurnLabel;
	[Export] public Label DefeatLabel;
	[Export] public Label VictoryLabel;
	[Export] public Label RestartLabel;

	[ExportGroup("Match")]
	/// <summary>
	/// The seed this match runs on: every random choice the simulation makes comes
	/// from it (docs/parity M0.5), so a match is reproducible from its seed and its
	/// command log. Zero means "pick one at match start" — the chosen seed is
	/// logged and written into the log, so a replay knows what to set.
	/// </summary>
	[Export] public int MatchSeed = 0;

	[ExportGroup("Content")]
	/// <summary>
	/// The roster this match draws from, and the units the build menu offers
	/// (docs/parity §2: unit data lives in resources, and there is one list of
	/// it). Assigned in main_scene.tscn.
	/// </summary>
	[Export] public UnitCatalog Catalog;

	/// <summary>
	/// The match's global rules — the numbers that are not per-unit and not part
	/// of the map (docs/parity §7), including the energy economy and the turn
	/// clock (M1). Assigned in main_scene.tscn.
	/// </summary>
	[Export] public MatchRules Rules;

	/// <summary>
	/// How this match's AI seats play: the enemy's aim, its power range and how
	/// long it thinks (docs/parity §7). Assigned in main_scene.tscn, and handed
	/// unchanged to <see cref="MatchSetup"/> — the AI numbers have one home and
	/// this is the reference to it, not a copy of it (§2).
	/// </summary>
	[Export] public AiProfile Profile;

	/// <summary>
	/// The map this match is fought on: its bounds, its pools and its start
	/// positions (docs/parity M0.4). Assigned in main_scene.tscn.
	/// </summary>
	[Export] public MapDefinition Map;

	// ------------------------------------------------------------------
	// The match's parts (docs/parity M0.6)
	//
	// Each owns one job and is built in _Ready: the world (the map and its
	// pools), the camera (view, panning, minimap), the turn flow, and the setup
	// that resolves the content and builds the combatants. GameManager remains
	// the match's entry point and the facade everything already calls — the
	// forwards below exist so call sites do not all have to change at once.
	// ------------------------------------------------------------------

	/// <summary>The world this match is fought in: its map, its bounds, its pools.</summary>
	public MatchWorld World { get; private set; }

	/// <summary>The match's view: the camera and the minimap.</summary>
	public MatchCamera Camera { get; private set; }

	/// <summary>The match's turn flow: the rotation, turn events, the round, the outcome.</summary>
	public TurnController Turns { get; private set; }

	/// <summary>The match's content and combatants.</summary>
	public MatchSetup Setup { get; private set; }

	/// <summary>The developer overlay: the live tuning surface (docs/parity M0.7).</summary>
	public DevOverlay Dev { get; private set; }

	/// <summary>
	/// The build menu (docs/parity M1): the pages, the unit buttons, the energy
	/// readout and the turn clock. Built at match start from the roster, like the
	/// minimap, and shown only for the human's turn.
	/// </summary>
	public BuildMenu Menu { get; private set; }

	/// <summary>
	/// The rules the simulation actually runs on (docs/parity §7): the assigned
	/// resource, or the fallback the setup created when nothing was assigned.
	///
	/// Everything that reads a rule reads it from here rather than from
	/// <see cref="Rules"/>, so an edit made under a running match — by the dev
	/// overlay, or by the editor writing the same resource instance — is what the
	/// next shot uses. The resource this returns is the one the tuning surface
	/// edits.
	/// </summary>
	public MatchRules ActiveRules => Setup?.Rules ?? Rules ?? MatchRules.Fallback;

	/// <summary>The scene's UI layer, shared with the parts that draw into it.</summary>
	public CanvasLayer UiLayer { get; private set; }

	/// <summary>The seed this match runs on (see <see cref="MatchSeed"/>).</summary>
	public int Seed => Setup?.Seed ?? 0;

	/// <summary>
	/// The match's own random number generator, seeded from <see cref="Seed"/>.
	/// Every random choice in the simulation must come from here rather than from
	/// the global generator: the global one is shared with the engine and the
	/// editor, so anything drawn from it depends on what else happened to run.
	/// </summary>
	public RandomNumberGenerator Random => Setup?.Random;

	/// <summary>
	/// The ordered log of everything this match has been told to do — the match's
	/// input, and what a replay is made of (docs/parity M0.5).
	/// </summary>
	public CommandLog Commands => Setup?.Commands;

	/// <summary>Every combatant in the match, in seat order. Read by the AI for its targets.</summary>
	public IReadOnlyList<Combatant> Combatants => Setup?.Combatants ?? System.Array.Empty<Combatant>();

	/// <summary>The combatant whose turn it currently is (null between turns).</summary>
	public Combatant ActiveCombatant => Turns?.ActiveCombatant;

	/// <summary>
	/// The round being played — **not** the turn: a round holds one turn per seat
	/// until every seat is out of turns, and energy is paid at its end (docs/parity
	/// M1). It is what <see cref="Structure.DisableFor"/> counts in and what the
	/// command log's ledger is read against.
	/// </summary>
	public int CurrentTurn => Turns?.Round ?? 1;

	/// <summary>
	/// Which turn of the round is being played, counting from 1. Turns alternate
	/// inside a round, so this is the number that moves with every launch; the
	/// round is the one that moves with every income payout.
	/// </summary>
	public int CurrentTurnNumber => Turns?.TurnNumber ?? 0;

	/// <summary>True while the player may pick units, aim, charge and fire.</summary>
	public bool IsPlayerTurn => Turns?.IsPlayerTurn ?? false;

	/// <summary>True while a turn's events are still resolving (control is locked).</summary>
	public bool IsResolvingTurn => Turns?.IsResolving ?? false;

	/// <summary>True once the match has been won or lost; input restarts the scene.</summary>
	public bool IsGameOver => Turns?.IsOver ?? false;

	/// <summary>The map's purple pools, or null when the map asks for none.</summary>
	public PoolField Pools => World?.Pools;

	/// <summary>The human's combatant: seat 0, the one the player drives.</summary>
	public Combatant HumanCombatant => Setup?.HumanCombatant;

	/// <summary>
	/// True while the player is choosing what to build (the menu is up), false
	/// while a chosen unit is being aimed (the launch view). A turn starts in the
	/// build view and returns to it after every shot (docs/parity M1).
	/// </summary>
	public bool IsBuildMode => _buildMode;

	/// <summary>The unit being aimed in the launch view, or null in the build view.</summary>
	public UnitDefinition ChosenUnit => _chosenUnit;

	public Structure SelectedStructure;

	/// <summary>
	/// Container that spawned chain roots are parented under. A combatant may
	/// spawn its root while the GameManager is still being readied, and neither
	/// the tree root nor the scene root accepts children at that point ("parent
	/// node is busy setting up children"); a node's own children never are.
	///
	/// Every structure in a match is spawned by its combatant at a start position
	/// the map gives (docs/parity M0.4) — nothing in the scene tree is adopted as
	/// a chain root any more.
	/// </summary>
	public Node3D StructureContainer { get; private set; }

	/// <summary>
	/// Where everything spawned during a match is parented: ammo in flight,
	/// explosions, and the structures ammo deploys (a combatant's own root is
	/// parented under <see cref="StructureContainer"/> directly).
	///
	/// Always a node *inside the scene*, never the tree root: restarting a
	/// finished match reloads the scene, and a reload frees the scene alone. A
	/// node parented to the tree root outlives the match and leaks into the next
	/// one — visible, unselectable, owned by a chain that no longer exists, and
	/// standing on the map doing nothing.
	///
	/// Falls back to the tree root only before the match has been set up.
	/// </summary>
	public Node SpawnContainer
	{
		get
		{
			if (IsInstanceValid(StructureContainer)) return StructureContainer;
			return GetTree()?.Root;
		}
	}

	/// <summary>
	/// How many physics steps this match has taken — the simulation's own clock, as
	/// opposed to frame time (docs/parity M0.5: a step that affects the simulation
	/// runs on the fixed physics timestep).
	///
	/// Deliberately not part of <see cref="StateHash"/>: the same command log takes
	/// the same steps to play out, but a run paced by wall-clock timers reaches a
	/// different count at the same command, and a fingerprint has to be about the
	/// state rather than about when it was taken.
	/// </summary>
	public int SimulationTicks { get; private set; }

	/// <summary>True in the build view (menu up) and false in the launch view (aiming).</summary>
	private bool _buildMode = true;

	/// <summary>The unit the player is aiming, or null in the build view.</summary>
	private UnitDefinition _chosenUnit;

	/// <summary>The page the build menu was last on, so a turn opens where the player left it.</summary>
	private int _lastPage = 1;

	/// <summary>Seconds left in the human's turn (docs/parity M1's turn clock).</summary>
	private float _turnClock;

	/// <summary>True when this match has a turn clock running for the human's seat.</summary>
	private bool _clockRunning;

	// The last energy readout shown, so the HUD only rebuilds when it changes.
	private int _lastHudEnergy = int.MinValue;
	private int _lastHudCap = int.MinValue;
	private int _lastHudIncome = int.MinValue;

	private float _currentAimAngle = 0f;
	private MeshInstance3D _aimIndicator;
	private float _currentPowerPercent = 0f;
	private bool _isCharging = false;
	private bool _isChargingUp = true;

	// Ids handed to structures as they register: the roots first, then whatever
	// shots deploy, in the order the simulation creates them — which is what makes
	// a command's structure id mean the same thing on a replay.
	private int _nextStructureId = 1;

	// Every Structure currently in the game, for locating surviving chain roots.
	private readonly List<Structure> _structures = new List<Structure>();

	// The recorded log a replay is playing, and how far through it we are.
	private CommandLog _replay;
	private int _replayIndex;


	// Guards against queueing the scene reload twice.
	private bool _restartRequested = false;

	public override void _Ready()
	{
		Instance = this;

		// The scene's UI layer, for the parts that draw into it.
		UiLayer = GetNodeOrNull<CanvasLayer>("../UI");

		// The match is built out of parts, each owning one job (docs/parity M0.6).
		// GameManager stays the entry point and the facade call sites already use,
		// and this is where they are assembled and wired.
		//
		// They are created here rather than authored as children in
		// main_scene.tscn because their settings still live on this node's exports,
		// which is what the scene overrides; the scene grows the explicit nodes
		// when M8 takes the HUD.
		// The container everything a match spawns is parented under. It exists before
		// the world is built: the pools are spawned into it.
		StructureContainer = new Node3D { Name = "Structures" };
		AddChild(StructureContainer);

		World = new MatchWorld { Name = "MatchWorld" };
		AddChild(World);
		World.Build(Map, MainCamera);

		Camera = new MatchCamera
		{
			Name = "MatchCamera",
			Camera = MainCamera,
			UiLayer = UiLayer,
			World = World,
		};
		AddChild(Camera);
		Camera.Build();

		Turns = new TurnController { Name = "TurnController" };
		AddChild(Turns);
		Turns.TurnStarted += OnTurnStarted;
		Turns.TurnCommitted += OnTurnCommitted;
		Turns.TurnEnded += OnTurnEnded;
		Turns.RoundEnded += OnRoundEnded;
		Turns.MatchEnded += OnMatchEnded;

		CreateAimIndicator();
		Setup = new MatchSetup
		{
			Name = "MatchSetup",
		};
		AddChild(Setup);

		// The build menu is the HUD's answer to a turn being a launch paid for out
		// of an energy purse (docs/parity M1). It is built here from the roster
		// rather than authored in the scene, like the minimap: a unit in the
		// catalog gets a button on its level's page without anyone editing the
		// scene.
		Menu = new BuildMenu();
		UiLayer?.AddChild(Menu);
		Menu.Build();
		Menu.UnitChosen += TryBuild;
		Menu.PageChosen += ChoosePage;
		Menu.EndTurnRequested += SkipTurn;
		Menu.CancelLaunchRequested += CancelLaunch;

		if (PowerBar != null) PowerBar.Visible = false;
		if (DefeatLabel != null) DefeatLabel.Visible = false;
		if (VictoryLabel != null) VictoryLabel.Visible = false;
		if (RestartLabel != null) RestartLabel.Visible = false;
		UpdateTurnLabel();

		// Build the match: the content, the seed and one chain per seat.
		Setup.Build(Catalog, Rules, Profile, MatchSeed, World, StructureContainer);

		// The tamper panel (docs/parity M0.7) is built once the match exists, so
		// it lists the resources this match is actually running on rather than the
		// ones the scene happens to point at.
		if (EnableDevOverlay)
		{
			Dev = new DevOverlay { Name = "DevOverlay" };
			AddChild(Dev);
			Dev.Build(Catalog, ActiveRules, Profile, World?.Map);
		}

		UpdateHud();

		// The first turn starts once the whole scene has finished setting up: the
		// starting structures' _Ready (which builds their highlight rings and
		// health bars) has not run yet at this point, so they could not be selected.
		GetTree().CreateTimer(0.0f).Timeout += () => Turns.Start(Setup.Combatants);
	}

	private void CreateAimIndicator()
	{
		_aimIndicator = new MeshInstance3D();

		// Create a long thin box that looks like a pointer
		var mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 2.0f) };
		_aimIndicator.Mesh = mesh;

		// Bright yellow material so it stands out
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 1, 0),
			ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded
		};
		_aimIndicator.MaterialOverride = mat;

		_aimIndicator.Visible = false;
		AddChild(_aimIndicator);
	}

	/// <summary>
	/// Selects one of the player's structures as the one to build from. A click on
	/// the world, the turn start, and the fall-back after a destruction all land
	/// here.
	///
	/// Selecting while a unit is being aimed returns to the build menu rather than
	/// leaving the player aiming from a structure they did not choose: the launch
	/// view belongs to one unit and one launcher.
	/// </summary>
	public void SelectStructure(Structure structure)
	{
		// No selecting while the turn's events are still resolving.
		if (!IsPlayerTurn) return;
		if (structure == null || !IsInstanceValid(structure) || structure.IsDestroyed) return;
		// Only the player's own chain is controllable — enemy structures are
		// never selectable, so a click can't hijack the opponent's chain.
		if (structure.OwnerChain != HumanCombatant) return;

		// The previous selection may have been destroyed; only touch it
		// while it is still a valid instance.
		if (SelectedStructure != null && IsInstanceValid(SelectedStructure))
		{
			SelectedStructure.SetHighlight(false);
		}
		SelectedStructure = structure;
		SelectedStructure.SetHighlight(true);

		// Move the camera to the structure being built from.
		Camera?.CenterOn(structure.GlobalPosition);

		// A new origin means a new shot: whatever was being charged is abandoned.
		_isCharging = false;
		_currentPowerPercent = 0f;
		if (PowerBar != null) PowerBar.Visible = false;
		HideAimIndicator();

		if (!_buildMode) EnterBuildMode(HumanCombatant);
	}

	/// <summary>
	/// The match's fixed step: everything that moves under physics — an ammo
	/// round's flight, a landing — advances here, never in _Process, so the same
	/// command log plays out identically whatever the frame rate is doing.
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		SimulationTicks++;
	}

	public override void _Process(double delta)
	{
		// The camera's own per-frame work: panning the player asked for, and the
		// minimap reconciled against the match's structures (docs/parity M0.6).
		Camera?.Pan((float)delta);
		Camera?.Refresh(_structures, SelectedStructure);

		// Safety net: never keep control pointed at a destroyed node.
		// If the selected node dies, control reverts to the highest
		// remaining node in the chain (or ends the game in defeat if none
		// remain).
		if (SelectedStructure != null && (!IsInstanceValid(SelectedStructure) || SelectedStructure.IsDestroyed))
		{
			if (!IsResolvingTurn)
			{
				RevertControlToHighestNode();
			}
			// During resolution the selection is re-established in OnTurnResumed().
			return;
		}

		// The turn clock ticks only while the player can actually act: it pauses
		// while a shot resolves, and does not run for a replayed match.
		if (TickTurnClock((float)delta)) return;

		// While the tamper panel is open it owns the keyboard: the menu, aiming
		// and charging are read from the Input singleton, which does not know an
		// event was handled, so a designer stepping through a tunable would
		// otherwise be playing at the same time (docs/parity M0.7).
		if (Dev != null && Dev.IsOpen) return;
		if (!IsPlayerTurn || IsReplaying || IsResolvingTurn) return;

		if (_buildMode)
		{
			HandleBuildInput();
		}
		else
		{
			if (Input.IsActionJustPressed("cancel_launch"))
			{
				CancelLaunch();
				return;
			}
			HandleAiming((float)delta);
			HandleFiringLogic((float)delta);
		}

		if (Menu != null && Menu.IsShowing) UpdateHud();
	}

	// ------------------------------------------------------------------
	// The player's turn: the build menu and the launch view (docs/parity M1)
	// ------------------------------------------------------------------

	/// <summary>
	/// Starts the human's part of a turn: make sure there is a launcher to build
	/// from, open the menu — or, when the energy cannot pay for anything, end the
	/// turn rather than sitting on a menu with nothing usable on it.
	/// </summary>
	private void BeginPlayerTurn(Combatant combatant)
	{
		_buildMode = true;
		_chosenUnit = null;

		EnsurePlayerOrigin(combatant);

		if (!CanBuildAnything(combatant))
		{
			GD.Print($"[Round {CurrentTurn}] {combatant.DisplayName}: {combatant.Energy} energy buys nothing — skipping the rest of the round.");
			SkipTurn();
			return;
		}

		EnterBuildMode(combatant);
	}

	/// <summary>
	/// Makes sure the selected structure is a live launcher of this combatant,
	/// falling back to the root Hub when it is not (the selection may have been
	/// destroyed during the last resolution).
	/// </summary>
	private void EnsurePlayerOrigin(Combatant combatant)
	{
		bool usable = SelectedStructure != null
			&& IsInstanceValid(SelectedStructure)
			&& !SelectedStructure.IsDestroyed
			&& SelectedStructure.OwnerChain == combatant
			&& IsLauncher(SelectedStructure);
		if (usable) return;

		Structure root = (combatant as NodeChainCombatant)?.FindRootStructure();
		if (root != null) SelectStructure(root);
	}

	/// <summary>Opens the build menu for the pot of energy the player is holding.</summary>
	private void EnterBuildMode(Combatant combatant)
	{
		_buildMode = true;
		_chosenUnit = null;
		_isCharging = false;
		_currentPowerPercent = 0f;
		if (PowerBar != null) PowerBar.Visible = false;
		HideAimIndicator();

		if (combatant != null && Menu != null)
		{
			Menu.ShowBuild(
				Catalog?.Buildable ?? System.Array.Empty<UnitDefinition>(),
				combatant.Energy,
				combatant.MaxEnergy,
				IncomeOf(combatant),
				_lastPage);
			_lastPage = Menu.Page;
		}

		UpdateHud();
	}

	/// <summary>Leaves the menu and aims the chosen unit: the power meter takes over.</summary>
	private void EnterLaunchMode()
	{
		_buildMode = false;
		_isCharging = false;
		_currentPowerPercent = 0f;
		_isChargingUp = true;

		Menu?.ShowLaunch(_chosenUnit);
		Menu?.SetPower(0f);
		if (PowerBar != null)
		{
			PowerBar.Visible = true;
			PowerBar.Value = 0d;
		}
		ShowAimPreview(SelectedStructure, _currentAimAngle, 0f);
	}

	/// <summary>
	/// Abandons the shot being aimed and returns to the menu. The unit was never
	/// launched, so nothing was spent: cancelling costs the player nothing but the
	/// time spent aiming.
	/// </summary>
	public void CancelLaunch()
	{
		if (!IsPlayerTurn || _buildMode) return;

		EnterBuildMode(ActiveCombatant);
	}

	/// <summary>Asks to build <paramref name="unit"/> from the selected structure.</summary>
	private void TryBuild(UnitDefinition unit)
	{
		if (!IsPlayerTurn || !_buildMode || unit == null) return;

		Combatant actor = ActiveCombatant;
		if (actor == null) return;

		if (!IsLauncher(SelectedStructure))
		{
			GD.Print($"[Build] {SelectedStructure?.Definition?.DisplayName ?? "That structure"} cannot launch units — only a Hub can.");
			return;
		}
		if (!actor.CanAfford(unit.Level))
		{
			GD.Print($"[Build] {unit.DisplayName} costs {unit.Level} energy; {actor.DisplayName} has {actor.Energy}.");
			return;
		}

		_chosenUnit = unit;
		EnterLaunchMode();
	}

	/// <summary>Switches the build menu to the page for <paramref name="level"/>.</summary>
	private void ChoosePage(int level)
	{
		if (!IsPlayerTurn || !_buildMode || Menu == null) return;
		if (Menu.SetPage(level)) _lastPage = Menu.Page;
	}

	/// <summary>The keyboard half of the build menu: pages, the unit cursor, end turn.</summary>
	private void HandleBuildInput()
	{
		if (Input.IsActionJustPressed("build_page_1")) ChoosePage(1);
		if (Input.IsActionJustPressed("build_page_3")) ChoosePage(3);
		if (Input.IsActionJustPressed("build_page_7")) ChoosePage(7);
		if (Input.IsActionJustPressed("switch_ammo_next")) Menu?.MoveCursor(1);
		if (Input.IsActionJustPressed("switch_ammo_previous")) Menu?.MoveCursor(-1);
		if (Input.IsActionJustPressed("choose_unit")) TryBuild(Menu?.CursorUnit);
		if (Input.IsActionJustPressed("end_turn")) SkipTurn();
	}

	private void HandleAiming(float delta)
	{
		float input = Input.GetAxis("aim_left", "aim_right");
		_currentAimAngle += input * ActiveRules.EffectiveAimRotationSpeed * delta;

		ShowAimPreview(SelectedStructure, _currentAimAngle, _currentPowerPercent);
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
	public void ShowAimPreview(Structure origin, float aimAngleRadians, float powerPercent, bool showPowerMeter = false)
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
			// The charge rate is the match's (docs/parity §7), so how long a player
			// has to place a shot is balance a designer can move, not a constant.
			float chargeSpeed = ActiveRules.EffectiveChargeSpeed;
			var effectiveChargeSpeed = _isChargingUp ? chargeSpeed : -1f * chargeSpeed;
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
			Menu?.SetPower(_currentPowerPercent);

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

			if (IsResolvingTurn)
			{
				// The shot is in the air: the menu stays out of the way until it
				// resolves, and then the turn is over — it was one launch.
				Menu?.HidePanels();
				HideAimIndicator();
			}
			else
			{
				// Nothing went out (the build was refused): back to the menu
				// rather than sitting on a launch view with no shot in it.
				EnterBuildMode(ActiveCombatant);
			}
		}
	}

	/// <summary>
	/// Fires the unit chosen in the menu along the player's current aim. Uses the
	/// exact same launch path as every AI combatant.
	/// </summary>
	/// <param name="powerPercent">Charge level, 0..1, mapped onto the launch force range.</param>
	private void Fire(float powerPercent)
	{
		Combatant actor = ActiveCombatant;
		UnitDefinition unit = _chosenUnit;
		if (actor == null || unit == null || SelectedStructure == null) return;
		if (!IsLauncher(SelectedStructure)) return;
		if (!actor.CanAfford(unit.Level)) return;

		SubmitCommand(MatchCommand.Launch(actor.Seat, unit.Id, SelectedStructure.Id, _currentAimAngle, powerPercent));
	}

	// ------------------------------------------------------------------
	// The turn clock (docs/parity M1, §7)
	// ------------------------------------------------------------------

	/// <summary>
	/// Starts the human's turn clock from the rules. A clock of zero means the
	/// match has none, and the HUD says so by showing nothing.
	/// </summary>
	private void ResetTurnClock()
	{
		MatchRules rules = ActiveRules;
		_clockRunning = rules != null && rules.HasTurnClock;
		_turnClock = rules?.EffectiveTurnClockSeconds ?? 0f;
		Menu?.SetClock(_turnClock, _clockRunning);
	}

	/// <summary>
	/// Runs the clock down for the time the player can act, and ends the turn
	/// when it hits zero. Returns true when the turn was ended.
	///
	/// The clock **pauses while a shot resolves** (the player cannot act, so it
	/// would be charging them for the game's own animation) and **does not run in
	/// a replay** (the recorded log already decides when the turn ended, and a
	/// clock running during a fast replay would turn matches into timeouts).
	/// The expiry is a <see cref="CommandKind.Skip"/> command, so the log records
	/// it and the replay ends the turn at exactly the same point.
	/// </summary>
	private bool TickTurnClock(float delta)
	{
		if (!_clockRunning)
		{
			Menu?.SetClock(0f, false);
			return false;
		}

		// A developer with the tuning panel open is not playing: the clock must not
		// cost them the turn while they move a number (docs/parity M0.7).
		if (!IsPlayerTurn || IsResolvingTurn || IsReplaying || ActiveCombatant == null) return false;
		if (Dev != null && Dev.IsOpen) return false;

		_turnClock = Mathf.Max(0f, _turnClock - delta);
		Menu?.SetClock(_turnClock, true);

		if (_turnClock > 0f) return false;

		_clockRunning = false;
		GD.Print($"[Round {CurrentTurn}] Turn clock ran out — ending the turn.");
		SkipTurn();
		return true;
	}

	// ------------------------------------------------------------------
	// Turn flow: the match's side of it
	// ------------------------------------------------------------------

	/// <summary>
	/// The rotation has given a seat its turn (docs/parity M0.6, M1). Who acts is
	/// decided here and nowhere else: a replayed command log, the human's input, or
	/// the combatant's own brain. The turn controller deliberately has no opinion —
	/// it announces the turn and waits for a command.
	/// </summary>
	private void OnTurnStarted(Combatant combatant)
	{
		if (combatant == null) return;

		if (IsReplaying)
		{
			// Neither input nor the AI: the log acts for every seat in turn
			// (docs/parity M0.5).
			Turns.IsPlayerTurn = false;
			HideAimIndicator();
			Menu?.HidePanels();
			FollowActingCombatant(combatant);
			UpdateTurnLabel();
			PlayNextReplayCommand(combatant);
			return;
		}

		if (combatant.IsPlayerControlled)
		{
			Turns.IsPlayerTurn = true;
			ResetTurnClock();
			GD.Print($"[Round {CurrentTurn}] {combatant.DisplayName}: turn with {combatant.Energy} energy.");
			BeginPlayerTurn(combatant);
		}
		else
		{
			// No input for this combatant — it acts on its own from here.
			Turns.IsPlayerTurn = false;
			HideAimIndicator();
			Menu?.HidePanels();
			FollowActingCombatant(combatant);

			GD.Print($"[Round {CurrentTurn}] {combatant.DisplayName}: turn with {combatant.Energy} energy.");
			combatant.BeginTurn();
		}

		UpdateTurnLabel();
		UpdateHud();
	}

	/// <summary>
	/// The turn committed — nothing the player had in progress survives it. The
	/// turn controller locks control; this is the player's half of that.
	/// </summary>
	private void OnTurnCommitted()
	{
		_isCharging = false;
		_currentPowerPercent = 0f;
		if (PowerBar != null) PowerBar.Visible = false;
		HideAimIndicator();
		Menu?.HidePanels();
	}

	/// <summary>
	/// The turn is over, however it ended: the menu and the aim arrow go with it.
	/// </summary>
	private void OnTurnEnded(Combatant combatant)
	{
		_isCharging = false;
		_currentPowerPercent = 0f;
		_clockRunning = false;
		if (PowerBar != null) PowerBar.Visible = false;
		HideAimIndicator();
		Menu?.HidePanels();
		UpdateTurnLabel();
	}

	/// <summary>
	/// The round is ending: pay income to every seat (docs/parity M1). The
	/// rotation announces it; the economy is the setup's, which is where the rules
	/// and the combatants both live.
	/// </summary>
	private void OnRoundEnded(int round)
	{
		Setup?.GrantRoundIncome(World);
		UpdateHud();
		UpdateTurnLabel();
	}

	/// <summary>
	/// The match is over: show the outcome and stop taking input except the
	/// restart. The state change itself belongs to the turn controller.
	/// </summary>
	private void OnMatchEnded(bool playerWon)
	{
		SelectedStructure = null;
		HideAimIndicator();
		_clockRunning = false;
		if (PowerBar != null) PowerBar.Visible = false;
		Menu?.HidePanels();
		if (RestartLabel != null) RestartLabel.Visible = true;

		if (playerWon)
		{
			if (VictoryLabel != null) VictoryLabel.Visible = true;
			GD.Print("VICTORY: every enemy chain has been destroyed.");
		}
		else
		{
			if (DefeatLabel != null) DefeatLabel.Visible = true;
			GD.Print("DEFEAT: no structures remain in the chain.");
		}

		UpdateTurnLabel();
	}

	/// <summary>The human's combatant as a chain, for root lookups.</summary>
	private NodeChainCombatant HumanChain => HumanCombatant as NodeChainCombatant;

	/// <summary>
	/// Registers an event that must resolve before the acting seat's turn is over,
	/// and commits the turn when it is the first (docs/parity M0.5: a turn is not
	/// over until its shot has landed).
	/// </summary>
	public void RegisterTurnEvent(TurnEvent evt)
	{
		Turns?.RegisterEvent(evt);
	}

	/// <summary>
	/// Ends the acting seat's turn through the command log — the player's **Skip**
	/// (Space, the clock running out, or nothing affordable left). It is recorded
	/// like any other action, so a replay knows exactly where the turn ended and
	/// which energy was carried forward (docs/parity M1).
	/// </summary>
	public void SkipTurn()
	{
		Combatant actor = ActiveCombatant;
		if (actor == null || IsGameOver || IsResolvingTurn || IsReplaying) return;

		SubmitCommand(MatchCommand.Skip(actor.Seat));
	}

	/// <summary>
	/// Points the camera at whoever is acting, when that is wanted. An AI (or a
	/// replayed) combatant can be anywhere on the map; the player's own turn
	/// centers the camera back on their selection through SelectStructure().
	/// </summary>
	private void FollowActingCombatant(Combatant combatant)
	{
		if (!CenterCameraOnActingCombatant) return;
		if (combatant is not NodeChainCombatant chain) return;
		if (chain.ActionStructure == null) return;

		Camera?.CenterOn(chain.ActionStructure.GlobalPosition);
	}

	/// <summary>
	/// Any key press or click after the match has ended restarts it by reloading
	/// the scene. The reload is deferred by a frame so it never runs from inside
	/// a node that the reload is about to free.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsGameOver)
		{
			if (_restartRequested) return;

			bool pressed = (@event is InputEventKey key && key.Pressed && !key.Echo)
				|| (@event is InputEventMouseButton mouseButton && mouseButton.Pressed);
			if (!pressed) return;

			_restartRequested = true;
			GD.Print("Restarting the match.");
			GetTree().CreateTimer(0.0f).Timeout += () => GetTree().ReloadCurrentScene();
			return;
		}

		// Right-click is the mouse's cancel for the launch view, matching Esc.
		if (@event is InputEventMouseButton right && right.Pressed && right.ButtonIndex == MouseButton.Right)
		{
			if (IsPlayerTurn && !_buildMode) CancelLaunch();
		}
	}

	/// <summary>
	/// Hands control back to the topmost surviving node of the player's chain.
	/// Called after destruction so control never gets stuck on a dead node.
	/// </summary>
	private void RevertControlToHighestNode()
	{
		Structure highest = HumanChain?.FindRootStructure();
		if (highest == null)
		{
			Turns?.EndMatch(false);
			return;
		}
		SelectStructure(highest);
	}

	// ------------------------------------------------------------------
	// Commands: the match's input, and the only way it changes
	// ------------------------------------------------------------------

	/// <summary>
	/// The one way anything in a match changes: record the action, then apply it
	/// (docs/parity M0.5). Live play goes through here — the player's builds and
	/// the AI's alike — so the log is a complete account of the match, and applying
	/// the log is replaying it.
	/// </summary>
	public void SubmitCommand(MatchCommand command)
	{
		Commands?.Record(command);
		ApplyCommand(command);
	}

	/// <summary>
	/// Applies a command to the world. Nothing else may launch a unit, deploy a
	/// structure or spend energy: a replay <em>is</em> this method being called with
	/// a recorded log, and anything that changed the simulation outside it would
	/// make the log a lie.
	///
	/// A command that cannot be applied — a structure that is no longer there, a
	/// unit the roster does not have, a launcher that cannot pay — is reported and
	/// dropped, leaving the turn to end normally rather than hanging the rotation.
	/// </summary>
	public void ApplyCommand(MatchCommand command)
	{
		Combatant actor = FindCombatant(command.Seat);
		if (actor == null)
		{
			GD.PushError($"GameManager: cannot apply {command.Describe()} — the match has no seat {command.Seat}.");
			return;
		}

		// Only the seat whose turn it is may act. This is what makes a log a
		// transcript rather than a pile of actions: replaying it applies every
		// command on its owner's turn.
		if (Turns?.ActiveCombatant == null || Turns.ActiveCombatant != actor)
		{
			GD.PushError($"GameManager: cannot apply {command.Describe()} — seat {actor.Seat} is not the seat acting.");
			return;
		}

		switch (command.Kind)
		{
			case CommandKind.Skip:
				Turns?.EndTurn();
				return;

			case CommandKind.Launch:
			{
				Structure origin = FindStructure(command.OriginStructureId);
				if (origin == null)
				{
					GD.PushError($"GameManager: cannot apply {command.Describe()} — there is no live structure {command.OriginStructureId}.");
					return;
				}
				if (origin.OwnerChain != actor)
				{
					GD.PushError($"GameManager: cannot apply {command.Describe()} — structure {origin.Id} is not seat {actor.Seat}'s.");
					return;
				}
				if (!IsLauncher(origin))
				{
					GD.PushError($"GameManager: cannot apply {command.Describe()} — {origin.Definition?.DisplayName ?? "that structure"} cannot launch units.");
					return;
				}

				UnitDefinition unit = Catalog?.Find(command.UnitId);
				if (unit == null)
				{
					GD.PushError($"GameManager: cannot apply {command.Describe()} — the roster has no '{command.UnitId}'.");
					return;
				}
				if (!unit.CanBeBuilt)
				{
					GD.PushError($"GameManager: cannot apply {command.Describe()} — '{unit.Id}' is not buildable in this match.");
					return;
				}
				if (!actor.CanAfford(unit.Level))
				{
					GD.PushError($"GameManager: cannot apply {command.Describe()} — seat {actor.Seat} has {actor.Energy} energy and '{unit.Id}' costs {unit.Level}.");
					return;
				}

				// The launch is the expensive part and the only part that can still
				// fail (a scene that will not load, a launcher that died this frame):
				// charging for it only once it has happened keeps a failed build free.
				if (!AmmoLauncher.Launch(unit, origin, command.AimAngleRadians, command.PowerPercent,
					$"seat {command.Seat} {unit.DisplayName}"))
				{
					return;
				}

				actor.SpendEnergy(unit.Level);
				UpdateHud();
				return;
			}

			default:
				GD.PushError($"GameManager: cannot apply a {command.Kind} command.");
				return;
		}
	}

	/// <summary>The combatant holding <paramref name="seat"/>, or null when the match has none.</summary>
	public Combatant FindCombatant(int seat)
	{
		foreach (Combatant combatant in Combatants)
		{
			if (combatant != null && combatant.Seat == seat) return combatant;
		}
		return null;
	}

	/// <summary>True when <paramref name="structure"/> may launch units (docs/parity M1: only a launcher builds).</summary>
	private static bool IsLauncher(Structure structure)
	{
		return structure != null && structure.Definition != null && structure.Definition.IsLauncher;
	}

	/// <summary>
	/// True when <paramref name="combatant"/> can pay for at least one buildable
	/// unit. A turn with nothing affordable ends itself: a budget that cannot buy
	/// anything is a turn that is over (docs/parity M1's "spent out").
	/// </summary>
	public bool CanBuildAnything(Combatant combatant)
	{
		if (combatant == null) return false;

		IReadOnlyList<UnitDefinition> buildable = Catalog?.Buildable;
		if (buildable == null) return false;

		foreach (UnitDefinition unit in buildable)
		{
			if (unit != null && unit.Level > 0 && combatant.CanAfford(unit.Level)) return true;
		}
		return false;
	}

	/// <summary>
	/// What <paramref name="combatant"/> will be paid at the end of this round:
	/// the main Hub plus its Harvesters, read live from the rules so the readout
	/// and the payout never disagree (docs/parity M1, §7).
	/// </summary>
	public int IncomeOf(Combatant combatant)
	{
		return combatant is NodeChainCombatant chain
			? chain.ComputeIncome(ActiveRules, Pools)
			: 0;
	}

	/// <summary>
	/// How many live structures are standing. A cheap progress signal for tests and
	/// diagnostics: it changes when the match does, and not when the engine ticks.
	/// </summary>
	public int LiveStructureCount
	{
		get
		{
			int count = 0;
			foreach (Structure structure in _structures)
			{
				if (structure != null && IsInstanceValid(structure) && !structure.IsDestroyed) count++;
			}
			return count;
		}
	}

	/// <summary>The live structure with the given id, or null when it is gone.</summary>
	public Structure FindStructure(int id)
	{
		foreach (Structure structure in _structures)
		{
			if (structure != null && IsInstanceValid(structure) && !structure.IsDestroyed && structure.Id == id)
			{
				return structure;
			}
		}
		return null;
	}

	/// <summary>True while a recorded log is acting for every seat instead of input and the AI.</summary>
	public bool IsReplaying => _replay != null;

	/// <summary>
	/// Hands the match a recorded log to play instead of taking input or letting
	/// the AI decide (docs/parity M0.5). Each seat's turn takes the next logged
	/// command for that seat, in order — a build, or the skip that ends the turn.
	///
	/// A log that does not fit the match — a command for a seat that is not acting,
	/// a structure that is not there — ends the replay and says so, because a
	/// replay that quietly diverges from the match it recorded is worse than one
	/// that stops.
	/// </summary>
	public void StartReplay(CommandLog log)
	{
		if (log == null || log.Count == 0)
		{
			GD.PushError("GameManager: there is nothing to replay.");
			return;
		}

		_replay = log;
		_replayIndex = 0;
		log.StopRecording();

		GD.Print($"[Replay] {log.Describe()}.");
	}

	private void PlayNextReplayCommand(Combatant combatant)
	{
		if (combatant == null) return;

		// The log is the whole of what happened: play it until it asks for a shot
		// (which has to resolve first), ends the turn, or runs out.
		while (true)
		{
			if (_replay == null) return;

			if (_replayIndex >= _replay.Count)
			{
				GD.Print("[Replay] the log is spent — the match carries on by itself from here.");
				_replay = null;
				OnTurnStarted(combatant);
				return;
			}

			MatchCommand command = _replay[_replayIndex];
			if (command.Seat != combatant.Seat)
			{
				GD.PushError($"[Replay] the log has {command.Describe()} next, but seat {combatant.Seat} ('{combatant.DisplayName}') "
					+ "is the one acting — the log does not fit this match. Stopping the replay.");
				_replay = null;
				Turns?.EndTurn();
				return;
			}

			_replayIndex++;
			GD.Print($"[Round {CurrentTurn}] {combatant.DisplayName}: replaying {command.Describe()}.");
			ApplyCommand(command);

			// The shot has to land before the turn is over; the rotation then gives
			// the next turn to the next seat, which comes back here through
			// OnTurnStarted.
			if (IsResolvingTurn) return;

			// A skip has moved the match on to another seat's turn.
			if (Turns?.ActiveCombatant != combatant) return;

			// A command that registered no event left the turn open: take the log's
			// next command for this seat, or end the turn when the log has moved on.
			if (_replay == null) return;
			if (_replayIndex >= _replay.Count) continue;
			if (_replay[_replayIndex].Seat != combatant.Seat)
			{
				Turns?.EndTurn();
				return;
			}
		}
	}

	/// <summary>
	/// A fingerprint of the simulation: the round, the seed, every combatant's
	/// energy, and every live structure's id, unit, hit points, parent and
	/// position (in id order) — docs/parity §6.
	///
	/// Two runs of the same match — from the same seed and the same command log —
	/// must produce the same hash. That is what turns "a replay is faithful" from a
	/// claim into a test, and since M1 it covers the energy economy too: a change
	/// to the income or a cost that did not reach the simulation would leave the
	/// same hash, and a change that did reach it moves this one. Positions are
	/// rounded before hashing so the fingerprint is about the simulation and not
	/// about the last bit of a float.
	///
	/// Take it between turns, while nothing is resolving: a destruction cascade in
	/// flight is a state no two observers would catch at the same moment.
	/// </summary>
	public string StateHash()
	{
		var structures = new List<Structure>();
		foreach (Structure structure in _structures)
		{
			if (structure != null && IsInstanceValid(structure) && !structure.IsDestroyed) structures.Add(structure);
		}
		structures.Sort((a, b) => a.Id.CompareTo(b.Id));

		var text = new StringBuilder();
		text.Append("round=").Append(CurrentTurn).Append(";seed=").Append(Seed);

		// The energy economy is part of the state now: what each seat holds, and
		// whether it is still in the match at all.
		foreach (Combatant combatant in Combatants)
		{
			if (combatant == null) continue;
			text.Append(";s").Append(combatant.Seat)
				.Append(":e").Append(combatant.Energy)
				.Append(":t").Append(combatant.Team);
		}

		foreach (Structure structure in structures)
		{
			Vector3 position = structure.GlobalPosition;
			int parent = structure.ParentCord != null
				&& IsInstanceValid(structure.ParentCord)
				&& structure.ParentCord.ParentStructure != null
					? structure.ParentCord.ParentStructure.Id
					: 0;

			text.Append('|').Append(structure.Id)
				.Append(':').Append(structure.Definition?.Id ?? "?")
				.Append(":hp").Append(structure.Health)
				.Append(":from").Append(parent)
				.Append(":at").Append(Round(position.X)).Append(',')
				.Append(Round(position.Y)).Append(',')
				.Append(Round(position.Z));
		}

		// Printed, not just hashed: when a hash changes, this is what changed.
		GD.Print($"[State] {text}");

		return Fnv1a64(text.ToString());
	}

	/// <summary>World coordinates are compared to a tenth of a millimetre, culture-invariantly.</summary>
	private static string Round(float value)
	{
		return value.ToString("0.####", CultureInfo.InvariantCulture);
	}

	/// <summary>FNV-1a over the state text: small, stable, and not a security claim.</summary>
	private static string Fnv1a64(string text)
	{
		ulong hash = 14695981039346656037UL;
		foreach (char c in text)
		{
			hash ^= c;
			hash *= 1099511628211UL;
		}
		return hash.ToString("x16");
	}

	// ------------------------------------------------------------------
	// Node registry
	// ------------------------------------------------------------------

	public void RegisterStructure(Structure structure)
	{
		if (structure == null || _structures.Contains(structure)) return;

		// The id is the command log's handle on this structure, so it is assigned
		// here — where the simulation learns the structure exists — and never moves.
		structure.AssignId(_nextStructureId++);
		_structures.Add(structure);
	}

	public void UnregisterStructure(Structure structure)
	{
		_structures.Remove(structure);
	}

	/// <summary>
	/// Shows the round, which turn of it this is, and whose move it is. The two
	/// counters are separate on purpose (docs/parity M1): the turn moves with every
	/// launch, the round moves with every income payout.
	/// </summary>
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
			TurnLabel.Text = $"Round {CurrentTurn}";
			return;
		}

		string whose = ActiveCombatant.IsPlayerControlled ? "your move" : $"{ActiveCombatant.DisplayName}'s move";
		TurnLabel.Text = $"Round {CurrentTurn} · Turn {CurrentTurnNumber} — {whose} — {ActiveCombatant.Energy} energy";
	}

	/// <summary>
	/// Puts the human's energy, its cap and the next round's income on the menu,
	/// when any of them has changed. The income is recomputed live, so a Harvester
	/// that has just been deployed shows up in the projection as soon as it lands.
	/// </summary>
	private void UpdateHud()
	{
		Combatant human = HumanCombatant;
		if (human == null || Menu == null) return;

		int energy = human.Energy;
		int cap = human.MaxEnergy;
		int income = IncomeOf(human);
		if (energy == _lastHudEnergy && cap == _lastHudCap && income == _lastHudIncome) return;

		_lastHudEnergy = energy;
		_lastHudCap = cap;
		_lastHudIncome = income;
		Menu.SetEnergy(energy, cap, income);
	}
}

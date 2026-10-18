# AGENTS.md

Agent guide for the **MbcPrototype** repository — a turn-based strategy
prototype built with **Godot 4.5.1 (C#/.NET)**. Read this before editing.

## Project at a glance

- Engine: Godot 4.5.1 stable, GL Compatibility renderer, C# (.NET 8,
  `Godot.NET.Sdk/4.5.1`). Assembly name `MbcPrototype`.
- Single scene: `Scenes/main_scene.tscn` (`res://`) — a 3D world where the
  player and one AI enemy each control a chain of structures, build from a
  roster, and take turns. A shot resolves only after every in-flight event
  completes.
- **A turn is an energy budget** (docs/parity M1). Each seat holds energy (start
  7, cap 35), spends it on builds — a unit's level *is* its cost, 1/3/7 — and may
  keep building while it can pay. The turn ends when the seat **skips** (Space:
  the rest of the energy carries forward) or when nothing in the roster is
  affordable. A **round** is one turn from every seat; at its end the seats are
  paid their income (main Hub 7, Harvesters 3 on a pool / 1 off it), and the
  order **rotates left** so the seat that went first goes last next round. The
  human has a per-turn clock (60 s) whose expiry is a recorded skip.
- Both sides are the **same kind of thing** (`Combatant`): identical structures,
  the same roster, the same launch path, the same energy economy and the same
  defeat rule. The only difference is where the aim and power come from (input
  vs. the AI). See *Combatants & turns* below.
- Units are **data**: a `UnitDefinition` per unit in a `UnitCatalog` resource
  (see *Unit data*). There are three today — `hub`, `harvester` and `bomb` — and
  adding a fourth is a resource plus a behaviour, not a branch in the launcher.
- **Every gameplay number is data** (docs/parity §2): unit numbers on their
  `UnitDefinition`, the match's globals on `MatchRules`, the world's on the
  `MapDefinition`, the enemy's on an `AiProfile`. No balance number belongs in a
  `.cs` file, and none belongs in two places. F1 in a running match opens the dev
  overlay, which edits any of them live and writes them back — see *Tuning*.
- The map is **data**: a `MapDefinition` resource (`Resources/Maps/`) holds the
  play area, the height field, the energy pools and the two start positions
  (see *Map data*). One map is shipped and the whole of development happens on
  it. The ground *mesh* is still a 10000×10000 plane — the map's bounds say
  where the map is, and M3's wrapping world is what gives them teeth.
- **This prototype is the base for a *MoonBase Commander* (2002) spiritual
  sequel** — a game of our own in the same genre, with the original as the
  reference for how the mechanics should feel and its manual as the starting
  dataset. [docs/parity](docs/parity/README.md) is the plan and the
  architectural blockers; [docs/parity/units.md](docs/parity/units.md) is the
  original's eighteen units, energy economy and world rules transcribed from the
  manual. Read the plan before starting work that it already sequences — it
  names the foundations (data-driven unit definitions, the hit-point model, the
  cord graph, the energy/round loop) that later features depend on. Two things
  about it matter before you write code:
  - **Scope (§1):** a 1v1 match against one AI on one hardcoded map, twelve of
    the eighteen units, no wind, no water, no multiplayer. Read it before
    building anything the spec describes but the scope excludes.
  - **Tunability (§2):** the original's numbers are an opening bid, not our
    balance, and every gameplay number belongs in a resource a designer can
    change — never a literal in a `.cs` file, never in two places at once.

## Build & run

- Build: `dotnet build MbcPrototype.csproj` (Godot builds the same way when
  the project is opened/run).
- Run: Godot binary is at
  `C:\Users\jaden\Documents\Godot\Godot_v4.5.1-stable_mono_win64.exe`
  (a `..._console.exe` variant exists for console output). Launch with
  `--path <repo>`, optionally a scene path and `--quit-after N`.
- The Godot executable and `dotnet` live **outside** this workspace. In the
  DSH sandbox, running them requires a `danger-full-access` escalation and
  the user may decline — the human often tests the game themselves. Do not
  treat a declined Godot run as a code failure; rely on `dotnet build` plus
  careful review instead.
- Do not leave temporary probe/test scenes or `.gd` scripts in the repo
  (they also generate `.uid` files that must be removed).

## Scene structure (`Scenes/main_scene.tscn`)

```
World (Node3D)
├── DirectionalLight3D
├── Ground (StaticBody3D, 10000x10000 plane)
├── Camera3D            # orthographic (projection=1), size=30, ~45° tilt
├── GameManager (Node)  # GameManager.cs — the main controller/singleton
└── UI (CanvasLayer)
    ├── FireStrengthBar (ProgressBar)      # bottom, full width — the launch
    │                                      #   power meter
    ├── Label                              # control hints, top-left
    ├── TurnLabel                          # top-right, round, whose move, energy
    ├── DefeatLabel                        # centered, hidden until defeat
    ├── VictoryLabel                       # centered, hidden until victory
    └── RestartLabel                       # centered, hidden until game over
```

Combatants are **not** in the scene, and neither is a starting structure:
`MatchSetup.CreateCombatants()` makes one combatant per seat the map defines and
each spawns its own root at that seat's start position (docs/parity M0.4). Roots
are parented under `GameManager.StructureContainer` (the tree root and the scene
root both reject `AddChild` while the scene is still setting up), as is
everything else spawned during a match — see *Key mechanics & invariants*.

The match's parts (world, camera, turn controller, setup) are created and wired
by `GameManager._Ready()` and are its children at runtime; they are not authored
in the scene yet, because their settings still live on `GameManager`'s exports
and M8 is what gives the HUD its own nodes.

The minimap is **built at runtime** by `MatchCamera.CreateMinimap()` (a
`SubViewport` + top-down camera + `TextureRect` in the bottom-right corner),
not stored in the scene. See [docs/camera](docs/camera/README.md).

The **build menu** is built at runtime too, by `BuildMenu.Build()` from the
catalog: the energy/clock strip, the `1`/`3`/`7` pages, the page's unit buttons
and the End turn button, plus the launch view's unit/cost/power readout. A unit
added to the roster therefore gets a button on its level's page with no scene
edit, and a level with no unit gets no page. It is added under the scene's `UI`
CanvasLayer, so the scene does not list it.

## Core scripts

| File | Role |
| --- | --- |
| `Scripts/Core/GameManager.cs` | The match's entry point and **facade** (docs/parity M0.6): the player's build/launch input and the turn clock, the HUD, selection, the command log (including energy spending and skips) and the structure registry. It builds the parts below in `_Ready` and forwards to them, which is why call sites still say `GameManager.Instance`. |
| `Scripts/Core/MatchSetup.cs` | **Builds the match**: resolves the roster/rules, settles the seed and the command log, and creates **one combatant per seat the map defines** (seat 0 is the human's, the rest are AI). Owns `Catalog`, `Rules`, `Seed`, `Random`, `Commands`, `Combatants`. |
| `Scripts/TurnSystem/TurnController.cs` | **The turn flow** (M1: an energy budget, not one shot): the rotation, the round, the pending `TurnEvent`s and the outcome. It announces `TurnStarted`, `TurnResumed` (the same seat may build again), `TurnEnded`, `RoundEnded` and `MatchEnded`, and does not act for a seat — input, the AI or a replay does. |
| `Scripts/Presentation/BuildMenu.cs` | **The build menu** (docs/parity M1): the pages, the unit buttons, the energy and next-round income, the turn clock, and the launch view's unit/cost/power readout. Built in code from the roster; it owns no match state and only reports what the player asked for. |
| `Scripts/Presentation/MatchCamera.cs` | **The view**: centering, edge panning, and the whole minimap (widget, coordinates, structure markers, pool markers, connection lines). Read-only over the match: it is handed the structures each frame. |
| `Scripts/World/MatchWorld.cs` | **The world**: the map it loaded, the bounds the minimap and the pools use, the pools themselves, and where each seat starts. |
| `Scripts/Core/Structure.cs` | A unit on the map: hit points from its definition, highlight ring, health-bar SubViewport sprite (a divider line per point of health), the cords it hangs from and anchors, destruction cascade, and the disabled state (`DisableFor`, the Harvester-income check; M4's EMP drives it). Knows its owning `OwnerChain`. |
| `Scripts/Core/Cord.cs` | **The link in the network**: the geometry between two structures (parent, child, length) plus the cable mesh. Built by the child structure in `_Ready`, parented under it, and what the destruction cascade runs down. |
| `Scripts/Core/PlayerCombatant.cs` | The human's combatant: a `NodeChainCombatant` whose aim/charge/fire comes from input in `GameManager`. |
| `Scripts/TurnSystem/Combatant.cs` | Abstract turn participant (team, defeat rule, `BeginTurn`/`EndTurn`) **and the energy purse**: current energy, its cap from the rules, `CanAfford`/`SpendEnergy`, the round's income grant. The rotation only ever talks to this. |
| `Scripts/TurnSystem/NodeChainCombatant.cs` | Combatant built from a chain of structures: structure registry, root lookup (`FindRootStructure()`, which follows the cords), defeat check, `Launch()`, `SubmitSkip()`, launcher lookup (`FindLauncherNearestTo`, `CanLaunchFrom`), the round's `ComputeIncome()` and the `RootUnit` definition its root spawns with. |
| `Scripts/Enemies/EnemyCombatant.cs` | The AI enemy: spends its turn's energy like the player — plans the first affordable buildable unit (working through the roster), fires it after its `Profile`'s think time, plans the next when the shot resolves, and skips when nothing is affordable. Every number it plays by is the profile's, not its own. |
| `Scripts/Units/UnitDefinition.cs` | **What a unit is**: the resource holding its numbers — level/energy cost, hit points, damage, blast radii, vision, build permission, icon, and the ammo scene. It replaces the old `AmmoType` enum. |
| `Scripts/Units/UnitCatalog.cs` | The roster: the one list of units the HUD, the launcher and the AI read. `Find(id)`, `Buildable`, `Validate()`. |
| `Scripts/Units/UnitTraits.cs` | `UnitTraits { Structure, Weapon, Launcher, Hub, Harvester }` — what a unit is, for code that does not know it by name. |
| `Scripts/Rules/MatchRules.cs` | (see *Tuning*) the match's global numbers: the blast curve, the ballistics, the aim/charge speeds, **the energy economy** (cap, starting purse, main-Hub and Harvester income) and **the turn clock**. |
| `Scripts/Enemies/AiProfile.cs` | **How an AI plays** (§7): think time, aim spread, the power range it fires at. One resource read by the combatant itself — the four numbers used to be exported on `GameManager`, `MatchSetup` *and* `EnemyCombatant` at once. |
| `Scripts/World/MapDefinition.cs` | **The map**: bounds, height field and terrain states, energy pools, and the start positions each seat of a match begins at. Builds its own `PoolField`. |
| `Scripts/World/TerrainState.cs` | `TerrainState { Ground, Plateau, Mountain }` — three states, not the original's four: water is out of the first slice. |
| `Scripts/Combat/AmmoLauncher.cs` | The single launch path for both sides: takes a `UnitDefinition`, instantiates its scene, hands it an `AmmoLaunch`, registers the `TurnEvent`, and returns whether the shot happened (the match charges for a build only on true). |
| `Scripts/Combat/ILaunchedAmmo.cs` | The behaviour interface every ammo scene's root implements — what replaced the launcher's `if (instance is Projectile) … else if (instance is Bomb)` chain. |
| `Scripts/Combat/AmmoLaunch.cs` | The struct a shot is configured with: unit, match rules, origin, velocity, turn event. |
| `Scripts/Combat/Projectile.cs` | Ammo: a ballistic body that deploys a `Structure` carrying its unit's definition where it lands, and resolves a `TurnEvent`. |
| `Scripts/Combat/Bomb.cs` | Ammo: a ballistic body that detonates on contact and damages every `Structure` in its unit's blast radii (see *Unit data & the blast*), resolving a `TurnEvent`. |
| `Scripts/Combat/BlastVisual.cs` | A blast's visuals: expanding translucent spheres over the damage radii, plus a ground marker (disc + a ring per radius) showing where they landed. Built in code, spawned by `Bomb`. |
| `Scripts/TurnSystem/TurnEvent.cs` | One unit of turn resolution; control returns to the acting seat when all pending events resolve. |
| `Scripts/World/PoolField.cs` | The map's purple pools: a fixed, grid-free scatter over the ground, one per-frame loop that pulses them all, and the pool queries future collision checks will use. |
| `Scripts/World/PurplePool.cs` | One purple pool: a solid rounded square of energy with a rim of dead grass around it, pulsing between a resting violet and a bright glow. Knows its own footprint (`ContainsXZ`/`OverlapsXZ`). |
| `Scripts/Simulation/MatchCommand.cs` | **One action, as data**: a `Launch` (seat, unit id, origin structure id, aim, power) or a `Skip` (seat only — end the turn, carry the energy). Serializes to a dictionary; names things by id, never by reference. |
| `Scripts/Simulation/CommandLog.cs` | **The match's input**: every action in the order it was applied, with the seed and map, written and read as JSON. Applying is GameManager's job, not the log's. |
| `Scripts/Testing/MatchHarness.cs` | The headless regression harness (`Scenes/harness.tscn`): plays a match with nobody at the keyboard, records its log, prints a state hash, and can replay a log and assert the hash. `--tune=target.Property=value` changes a number before the match runs; `--aim=pools` aims structure builds at pools (the Harvester income path). |
| `Scripts/Testing/TuningSurface.cs` | **The tuning surface**: every number the match's resources export, read and written by name, saved back to the `.tres` files. Built by walking the resources' own property lists, so a number added to any of them shows up with no list to maintain. Drives both the overlay and `--tune`. |
| `Scripts/Testing/DevOverlay.cs` | **The F1 dev overlay**: a panel over the tuning surface — arrows pick and change a number live, Enter writes it back to its resource, `R` re-reads the files. Built in code, and it owns the keyboard while it is open. |
| `Shaders/CableShader.gdshader` | Visual cable between chained nodes. |

## Unit data (`Resources/`)

**A unit is a resource, not a class.** Everything the game knows about one is on
its `UnitDefinition`; the roster is a `UnitCatalog` that references those files.
This is docs/parity §2's "no gameplay number in a `.cs` file" and M0.1, and it is
the seam every later unit depends on.

```
Resources/Units/hub.tres            # Hub: level 7, 5 hit points, the launcher
                                    #   and the main Hub (income) — Traits 13,
                                    #   LaunchForceScale 1.0 (see *Tuning*)
Resources/Units/harvester.tres      # Harvester: level 7, 5 hit points, energy
                                    #   income — Traits 17 (Structure|Harvester)
Resources/Units/bomb.tres           # Bomb: level 1, damage 3, radii 1.5 / 3.0
Resources/Units/unit_catalog.tres   # the roster: hub, harvester, bomb
                                    #   (references, not copies)
Resources/Rules/match_rules.tres    # the match's global numbers (blast curve,
                                    #   ballistics, aim/charge, energy, clock)
Resources/AI/ai_profile.tres        # how the AI seats play (think, aim, power)
```

- `GameManager.Catalog`, `GameManager.Rules`, `GameManager.Profile` and
  `GameManager.Map` are the references, assigned in `main_scene.tscn` and handed
  to `MatchSetup.Build()`. It validates the catalog at match start (duplicate
  ids, a unit with nothing to launch) — a content error is reported there rather
  than swallowed.
- A definition's `ProjectileScene` is its **behaviour**: that scene's root script
  implements `ILaunchedAmmo`, so the launcher configures it without knowing what
  it is. `UnitTraits` (`Structure`, `Weapon`, `Launcher`, `Hub`, `Harvester`) is
  what *other* code branches on — so a Harvester is a structure that pays income,
  and only the Hub is a launcher, without anything testing for the id `"hub"`.
- The build menu's pages and buttons are built from `Catalog.Buildable` (which
  is rebuilt on every read, so a live `CanBeBuilt` change is obeyed), in roster
  order and grouped by level, so an icon and a unit can never disagree.
  `switch_ammo_next`/`switch_ammo_previous` (Q/E) move the menu's cursor.
- **Adding a unit**: a new `.tres` referencing a new ammo scene, plus an entry in
  `unit_catalog.tres`. Nothing in the launcher, the HUD or the turn code changes
  — its level becomes a page if that level is new, its cost is its level, and it
  is affordable or not according to the player's energy.
- **Income is a trait, the amounts are rules** (docs/parity M1, §7): a root
  structure with the `Hub` trait pays `MatchRules.MainHubIncome`; every live
  structure with the `Harvester` trait pays `HarvesterIncomeOnPool` or
  `HarvesterIncomeOffPool` according to whether its centre stands in a pool, and
  nothing while it is disabled. `NodeChainCombatant.ComputeIncome()` is the one
  place that adds it up, and both the payout and the HUD's "next round +N" read
  it, so the projection and the payment cannot disagree.
- Godot does not write a value into a resource when it equals the script's
  default, so **a default must never be a real number**: `Level` defaults to 0
  ("no level set") and `CanBeBuilt` to false, and the three shipped units state
  their real values. A default that were a real cost would be the same number
  living in two places (§2). The same rule holds for `MatchRules`,
  `MapDefinition` and `AiProfile`, whose `Validate()` reports what is still
  unset — so an unset number is loud at match start, and the code fallbacks that
  keep the project running with no content are never a second opinion.
- The `hub.tres`/`harvester.tres`/`bomb.tres` split is also where hit points live
  (§M0.2): a structure's hit points are its definition's `HitPoints` (Hub 5,
  Harvester 5; a Bomb has none), and there is no longer one number for every
  structure.
- **Reach is per launcher**: `LaunchForceScale` multiplies the match's force
  range, and `AmmoLauncher` reads it off the definition of the *structure it is
  firing from*, not off the ammo. A weapon never states one; the Hub states 1.0.
  The interpolation itself lives in `MatchRules.LaunchForce()`, so a second
  consumer (M6's range estimate, M3's height→range curve) asks rather than
  repeats it.

## Map data (`Resources/Maps/`)

**The map is a resource too.** `MapDefinition` holds the play area, the height
field, the energy pools and the start positions; `GameManager.Map` is the one
reference, assigned in `main_scene.tscn`, handed to `MatchWorld.Build()`, and
validated there at match start (a size, two start positions, a height field that
matches its grid).

```
Resources/Maps/development_map.tres   # 475x475, flat, pool side 6 at 1% coverage,
                                      #   seats at (-50, 0) and (50, 0)
```

- **Bounds** (`Center`, `Size`) are the play area in world XZ. `MatchWorld.Bounds`
  is what `MatchCamera.WorldRect` returns, so the minimap shows the map and
  rescales with it; `Extent` is the larger axis, which is the minimap camera's
  ortho size.
- **The height field** (`GridWidth`/`GridHeight`, `Terrain`, `TerrainHeights`) is
  painted but **flat**: the development map's one cell is `Ground`, and the
  per-state heights (ground 0, plateau 2, mountain 4 — a first guess, §7) are read
  by nothing until M3. Cells hold *states*, not heights, so what a plateau is
  worth is one number rather than one per cell. Three states, not four: there is
  no water (§1).
- **Pools**: `MapDefinition.CreatePoolField()` scatters them over the map's own
  bounds, so pools, minimap and start positions agree on where the map is. Zero
  `PoolSide` or coverage means a map with no pools. `MatchWorld.Build()` calls it
  and nothing else knows a pool's size.
- **Start positions** are seats, in order: seat 0 is the human's, seat 1 the
  AI's, and `MatchSetup.CreateCombatants()` spawns each chain's root at its
  seat's position (`MatchWorld.SeatPosition`). Nothing about where a match begins is authored in the scene or
  derived from the window any more (docs/parity M0.4) — M6's setup screen chooses
  between maps rather than positions.
- **Adding a map**: a new `.tres`. One is shipped and all of development happens
  on it; M9's editor is what authors more.

## Combatants & turns

Authoritative flow: `MatchSetup` builds the match, `TurnController` runs the
rotation, and `GameManager` decides who acts for each seat.

- `MatchSetup.CreateCombatants()` is the **only** place opponents are declared,
  and it makes one per seat the map defines — seat 0 player-controlled, every
  other seat an AI on a team of its own. A map with three seats is a three-way
  match; `1v1` is what today's map says, not an assumption in the code (§1,
  *Built for expansion*). Adding a different *kind* of combatant means deriving
  from `Combatant` in there.
- **A seat's turn is an energy budget** (docs/parity M1). `TurnController.StartTurn()`
  announces `TurnStarted`; the seat builds while it can pay; each build registers a
  `TurnEvent`, and when the last one resolves the controller announces
  `TurnResumed` — the **same** seat may build again. The turn ends only when a
  `Skip` command is applied (`GameManager.SkipTurn()` submits it: Space, the clock
  running out, or nothing affordable) — `EndTurn()` then advances the round order.
  `GameManager.OnTurnStarted`/`OnTurnResumed` decide who acts: a replayed command
  log, the human (`IsPlayerTurn = true`, a launcher selected, the menu open) or the
  combatant's own brain (`BeginTurn()`).
- `TurnController` also raises `TurnCommitted` (GameManager cancels a charge in
  progress and hides the menu), `TurnEnded` (the seat is done) and `MatchEnded`
  (GameManager shows the outcome labels).
- `OnEventResolved()` runs when the pending-event list drains: it evaluates the
  match and otherwise queues the *resume*, and `EndTurnNow()` queues the *advance*.
  Both are **deferred to idle time** (`QueueResume()`, `QueueAdvance()`), because
  an event can resolve from inside `_ExitTree` where the tree is locked (see
  *Determinism*).
- `EndRound()` is reached when every seat in the round order has had its turn: it
  raises `RoundEnded` (the match pays income), increments `Round`, rebuilds the
  round order **rotated left by one seat** and starts the next round's first turn.
- An AI combatant **must** either submit a build during its turn or submit a skip
  (`NodeChainCombatant.SubmitSkip()`), otherwise the rotation would wait forever.
  `BeginTurn()` is called for the AI at the turn start and after every resolution.
- Win/lose: `TurnController.EvaluateOutcome()` ends the match when a whole
  `Team` has no combatant left that can act — `VICTORY` if only the player's team
  is left, `DEFEAT` otherwise. Because destroying a root cascades down its cords
  through the whole chain, "the enemy's root was destroyed" is exactly this check.
- `CurrentTurn` counts **rounds** (it advances at the end of a round), and the
  turn label shows the round, whose move it is and that seat's energy.
- Game over: `_UnhandledInput` restarts the match on any key/click by
  reloading the scene (deferred by a frame, so the reload never runs from
  inside a node it is about to free).

Team/targeting details:

- `Combatant.Team` (player 0, enemy 1) decides who may target whom; the AI
  picks the nearest hostile structure and fires from its own structure closest to
  it, so its chain creeps toward the player.
- `Structure.OwnerChain` is set **before** the structure enters the tree (by the
  spawning combatant, or by the projectile that deployed it) so `_Ready` can
  register it with the right chain. `SelectStructure()` only accepts the player's
  own chain, so clicks can't hijack the enemy's.
- A structure deployed by a projectile whose creator died mid-flight spawns
  **unowned**: it belongs to no combatant and keeps no defeated chain alive — and
  it hangs from nothing, so it is a root with no chain.
- **The chain is cords, not parent pointers.** A structure that was launched sets
  `ParentStructure` *before* it enters the tree; its `Cord` is built in its
  `_Ready` (`Cord.Connect`), because the cable is measured between two world
  positions and both ends have to be in the tree. The cord is parented under the
  structure that hangs from it, so it dies with that structure, and it is what
  `NodeChainCombatant.FindRootStructure()` (and therefore the defeat check) reads:
  a structure with no live cord above it is a root. Do not re-derive the link from
  `ParentStructure` — the cord is the link (docs/parity M0.3).

## Key mechanics & invariants

- **Turn system**: the first event registered on the `TurnController` commits the
  turn's resolution (`IsPlayerTurn=false`, `IsResolving=true`); control returns to
  the same seat in `TurnResumed` when the pending-event list drains (M1). The turn
  itself ends on a skip. `Structure.Destroy()` registers a `TurnEvent` resolved in
  `_ExitTree` so destruction cascades finish before the seat acts again. The
  cascade walks `ChildCords`: destroying a structure destroys everything its cords
  anchor, one generation per second.
- **Energy and the build**: a unit's `Level` *is* its cost (docs/parity M1, §7).
  `GameManager.ApplyCommand()` is the only place energy is spent: it checks the
  acting seat, the origin belongs to that seat and is a **launcher**, the unit is
  buildable and affordable, launches it through `AmmoLauncher` (which returns
  whether the shot happened), and only then calls `actor.SpendEnergy(unit.Level)`.
  A build that could not happen costs nothing and is reported. Nothing may spend
  energy or launch around that path.
- **Nothing but a launcher builds.** `UnitTraits.Launcher` is on the Hub only, so a
  Harvester — or any future structure without the flag — can stand in a chain but
  cannot fire. The check is in `ApplyCommand` (the simulation) *and* in the menu's
  `TryBuild`, so neither code nor a click can bypass it.
- **Income runs at the round end** (`MatchSetup.GrantRoundIncome`, driven by
  `RoundEnded`): a root `Hub` pays `MainHubIncome`, every live `Harvester` pays the
  on-pool or off-pool amount by whether its centre is in a pool, and a disabled
  Harvester pays nothing. Income is capped at `MaxStoredEnergy`, so unspent energy
  carries forward only up to the cap. The HUD's "next round +N" is the same
  `ComputeIncome()` call, so the projection cannot drift from the payout.
- **Destruction**: `IsDestroyed` (set immediately) vs
  `IsInstanceValid` (still true until `QueueFree` takes effect at end of
  frame). Always check the flag, never just instance validity.
- **Selection**: `SelectStructure()` only accepts a live structure of the
  player's own chain, re-centers the camera via a tween, and manages the
  highlight. When the selection dies, control reverts to the player's chain root
  (the structure with no live cord above it).
- **The player's turn is two views** (docs/parity M1). *Build*: the menu is up,
  1/3/7 pick a page, Q/E move the unit cursor, Enter or a click chooses a unit
  (`TryBuild` checks the launcher and the energy). *Launch*: the menu steps aside,
  A/D aim, holding `fire_shot` (W/Up) charges the power meter, release fires; Esc,
  right-click or the Cancel button go back to the menu without spending. After a
  shot resolves the player is back in the build view with the energy left.
- **Aiming**: `aim_left`/`aim_right` rotate `_currentAimAngle` (A/D + arrow
  keys); `fire_shot` charges a power bar; release launches the chosen unit.
  The AI drives the same `_currentAimAngle`-equivalent through
  `ShowAimPreview()` and `AmmoLauncher`, so both sides shoot identically.
- **The turn clock** (`MatchRules.TurnClockSeconds`, 0 = no clock) belongs to the
  human's seat and starts at each of its turns. It *pauses while a shot resolves*
  and does not run in a replay; when it runs out the match submits a skip, so the
  expiry is in the command log and a replay ends the turn at the same point.
- **Enemy tuning** is the `AiProfile` resource (`Resources/AI/ai_profile.tres`),
  read by the combatant itself: think time, aim spread (180° = fully random), and
  the power range it fires at. It used to be exported on three nodes at once —
  do not reintroduce that. `EnemyCombatant.Profile` is the reference;
  `AiProfile.Fallback` is a deliberately degenerate enemy for a match with no
  content. Where the two sides *start* is not tuning and is not here: it is the
  map's `StartPositions`. Camera centring on the acting seat
  (`CenterCameraOnActingCombatant`) is still on `GameManager`, because it is
  presentation and not balance.
- **Unit data & the blast**: every number a unit has lives on its
  `UnitDefinition` (see *Unit data*), and the only numbers elsewhere are the
  match's `MatchRules`. A weapon damages by distance, not by what it touched:
  full `Damage` inside the unit's `DirectHitRadius`, the rules' falloff share of
  it out to the unit's `OuterRadius`, nothing past it. Distance is measured along
  the ground (XZ) from where the shot went off, and **every** `Structure` in range
  takes the damage, the firing combatant's own chain included. `BlastVisual`
  spawns itself from `Bomb`, draws the same two radii as expanding translucent
  spheres and as discs/rings on the ground, and frees itself once its marker has
  faded.
- **Hit points**: `Structure.Health` starts at `MaxHealth`, which is its
  definition's `HitPoints` (Hub 5, Bomb none — a Bomb is not a structure).
  `TakeDamage` subtracts one point per point with **no armour threshold**, so
  partial damage is the normal case: a Bomb (3) on a Hub (5) leaves 2. A
  structure with no definition falls back to a constant and says so — that
  fallback is for running with no content, never the source of truth.
- **Nothing spawned during a match is parented to the tree root.** Chain roots,
  ammo in flight, explosions and the structures ammo deploys all go under
  `GameManager.SpawnContainer` (`StructureContainer`) — both *inside* the scene.
  Restarting a finished match reloads the scene, and a reload frees the scene
  alone: a structure left under the tree root outlives the match and comes back
  as a stale one nobody can select (its chain is gone), standing on the map.

## Determinism & the command log

A match is a function of **(map, seed, command log)** (docs/parity M0.5). That is
what makes replays, regression tests and "change a number, replay, compare"
possible, and it only holds if everything obeys four rules.

- **One way in.** `GameManager.SubmitCommand()` records the action and applies it;
  `GameManager.ApplyCommand()` is the only thing that changes the world — no code
  launches a unit, deploys a structure or spends energy around it. The player's
  shots and the AI's both go through `SubmitCommand`, which is why a log is a
  complete account of a match and replaying one *is* replaying the match.
- **Ids, not references.** A command names its actor (`MatchCommand.Seat`, the combatant's
  position in the rotation — not its team), its unit (`UnitDefinition.Id`) and its
  origin (`Structure.Id`). `Structure.Id` is handed out by
  `GameManager.RegisterStructure()` in the order the simulation creates things,
  which is the same order on every run of the same log. Never put an object
  reference in a command, and never renumber a structure.
- **Randomness comes from the match.** `GameManager.Random` is seeded from
  `GameManager.Seed`; simulation code must never call `GD.RandRange`/`GD.Randi`
  (the global generator is shared with the engine and the editor). Add a random
  choice by drawing from `GameManager.Instance.Random`.
- **The simulation runs on physics steps.** Anything that moves — an ammo round,
  a landing — is in `_PhysicsProcess` on the fixed step set in `project.godot`
  (60/s), never in `_Process`, and `SimulationTicks` counts the steps.

Two consequences worth knowing before changing the turn flow:

- **The rotation advances, and the same seat resumes, at idle time**
  (`QueueAdvance()` / `QueueResume()`), never on the spot. Either can be triggered
  from inside a node's `_ExitTree` — a destruction event resolves there — and the
  tree is locked while nodes are removed, so anything the next turn spawns (a
  replayed shot included) fails with "parent node is busy setting up children" and
  leaves a turn that can never resolve. If you add work to a turn, do not un-defer
  either call.
- **What is not deterministic yet**: the AI's `ThinkTime`, the cascade's 1.0 s per
  generation and the human's turn clock are wall-clock timers. The first two pace
  the match rather than change its outcome (the world is frozen while a turn
  resolves); the clock *does* decide when a turn ends, which is why its expiry is
  submitted as a recorded skip and why it does not tick in a replay. They are why
  the harness is run with `--fixed-fps 60`, and the cascade's pacing is M2's to
  fix.
  Float determinism holds for one build on one platform; a state hash compares
  runs, not releases.

`GameManager.StateHash()` fingerprints the round, the seed, each combatant's
energy and team, then the live structures (id, unit, hit points, parent,
position) in id order — so since M1 a replay also proves the energy economy (an
income or cost change that never reached the simulation would leave the hash
alone). Take it between turns, while nothing is resolving.

## Tuning: the resources, the overlay, and `--tune`

**The whole tuning surface is the match's resources** (docs/parity §2, §7, M0.7).
Four homes, and every number is in exactly one of them:

| what | home |
| --- | --- |
| one unit: hit points, damage, blast radii, **level (= energy cost)**, vision, build permission, **reach** (`LaunchForceScale`) | its `UnitDefinition` |
| the match: the blast falloff curve, the launch ballistics (gravity, force range, upward bias, muzzle height), the aim and charge speeds, **the energy economy** (cap, starting purse, main-Hub income, Harvester on/off-pool income) and **the turn clock** | `MatchRules` |
| the world: bounds, the height field and per-state heights, pool size and coverage, start positions | the `MapDefinition` |
| the AI: think time, aim spread, the power range it fires at | the `AiProfile` |

Two ways to move a number, both going through `TuningSurface`:

- **The F1 overlay** (`Scripts/Testing/DevOverlay.cs`), in a running match:
  up/down pick a row, left/right change it (Shift ×10, Ctrl ÷10), **Enter** writes
  every edited number back into its `.tres`, **R** re-reads the files, Esc closes.
  A change is live the moment it is made — the simulation reads these resources
  every time it fires — so the loop is "F1, move a number, close, shoot".
  `GameManager.EnableDevOverlay` turns it off (a screenshot, a recording).
- **`--tune=<target>.<Property>=<value>`** on the harness, for a headless or
  replayed match (`MatchHarness.cs`), optionally with `--save-tuning` to write the
  values back. Targets are `rules`, `ai`, `map` and each unit's id.

The surface is **built by walking each resource's exported properties**, so it
never needs updating when a number is added: an export shows up in the panel and
in `--tune` by existing. Only properties a resource's own script declares are
listed (`PROPERTY_USAGE_SCRIPT_VARIABLE`) — a `Resource` also reports
`resource_local_to_scene` and friends, and listing those would put a checkbox
that means nothing at the top of the panel.

Adding a number therefore means: export it on the right resource with an *unset*
default, state its real value in the `.tres`, report it from that resource's
`Validate()`, and read it through the resource everywhere it is used. Do not add
an export to `GameManager` for it — that is where the AI's four numbers and the
launch ballistics used to live, and §2 names the resulting duplication as a bug.

## Tests

`Scenes/harness.tscn` plays a real match headlessly: the harness is the player's
brain, the AI plays itself, and both act through the command log.

```sh
# record a 10-round match, print its state hash, write the log
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --turns=10 --seed=7 --record=/tmp/m.json
# replay it and assert it lands in the same state
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --replay=/tmp/m.json --expect=<hash>
```

```sh
# the same log, one number changed: a different hash is the proof it reached the
# simulation (docs/parity §2's "edit, replay, compare")
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --replay=/tmp/m.json \
    --tune=bomb.Damage=6
# the same for the economy: income and cost are numbers like any other
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --replay=/tmp/m.json \
    --tune=rules.MainHubIncome=20
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --replay=/tmp/m.json \
    --tune=harvester.Level=3
# and, to keep the change, write it back into the .tres the way the overlay does
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --turns=1 \
    --tune=bomb.Damage=6 --save-tuning
```

```sh
# M1's exit-criteria run: Hub and Harvester only, aimed at pools so the
# Harvester's on-pool income is exercised; the [Energy] lines are the ledger.
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --turns=4 --seed=7 \
    --aim=pools --tune=bomb.CanBeBuilt=0 --tune=map.PoolCoverageFraction=0.3
```

The recorded match's state hash depends on the match's numbers and content, so
an M1-era match is **not** expected to reproduce the M0 baseline
(`c6cd89abf0971af6`, 10 rounds, seed 7): a turn is now several builds and the
energy is part of the fingerprint. For reference, as of M1 the default 10-round,
seed-7 match (the three shipped units) hashes to `2c6d2a00819b84b2`, and the
Hub-and-Harvester run above to `14e62615617f13b9`; both replay to themselves.

`--fixed-fps 60` paces the run by frames rather than by the wall clock: it is much
faster, and the think-timers stop depending on real time. The harness exits
non-zero when an `--expect` hash does not match, and fails rather than hangs when
a match stops making progress (it prints what was still in the world). Change a
number in a `.tres`, replay a recorded log, and a different hash is the proof the
change reached the simulation.

`--tune` names a number the way the overlay shows it — `<target>.<Property>`, with
the targets `rules`, `ai`, `map` and each unit's id — and the labels it knows are
printed when one does not exist.

## Purple pools

`MatchWorld.Build()` lays a fixed `PoolField` of `PurplePool`s over the map's
ground at match start. Since M1 the **Harvester's income reads them** (a
Harvester on a pool earns 3 a round, 1 off it — `NodeChainCombatant.ComputeIncome`
asks `PoolField.IsInAnyPool` for the structure's centre); the other collision checks
they exist for (landing, build legality) are still not written.

- **The layout is fixed, not generated.** `PoolField.Scatter()` takes as many
  candidate positions as the coverage target asks for and keeps the ones with
  room; a candidate is a point in the unit square from a fixed integer hash of its
  index (`PoolField.CandidateAt` / `Hash01`). There is no seed, no generator and no
  stored table anywhere in the two pool scripts — the hash *is* the map, and it
  gives the same pools in the same places on every run. It is deliberately not a
  grid: nothing lines up, which is also why it is not a low-discrepancy sequence
  (Halton's points land on a lattice in each axis taken alone). The numbers are
  the map's: `MapDefinition.PoolSide` (three structure diameters — a structure's
  radius is 1.0, so 6.0) and `MapDefinition.PoolCoverageFraction` (1/100 on the
  development map: the count follows from it, giving 63 pools).
- **The field** is the map's own `Bounds` (docs/parity M0.4): the map scatters
  its pools over itself, so the pools, the minimap and the start positions all
  agree on where the map is. A zero `PoolSide` or coverage means a map with no
  pools.
- **No two pools touch.** `Scatter()` rejects any candidate within
  `side × 1.2` of a placed pool — footprints plus both rims — so pools never
  overlap, every one can sit at the same height, and the coverage counted really
  is ground that is pool. (An authored layout that *did* overlap pools would need
  a small per-pool height spread instead: two coincident surfaces z-fight into a
  flicker.)
- **The shape** is a rounded square of solid energy with a rim of dead, sandy
  grass around it. There is no rounded-square primitive, so `PurplePool.CreateMesh()`
  builds one once per pool size (two surfaces: the sandy rim with its own fixed
  material, and the pool left bare for each pool to override with the material it
  pulses) and `PoolField` hands that one mesh to every pool on the map. The rim is
  drawn *outside* the pool's footprint: `Side`, `ContainsXZ` and `OverlapsXZ` are
  the energy, not the sand, so the rim changes the map's looks and not its
  geometry — and `CoveredFraction` counts only the pools.
- **The effect**: the pool is opaque and always drawn, and the pulse is its
  *colour* — a rise from a resting violet to a bright glow, a hold, a fall back,
  then a rest, all on the same 5-second cycle; one `_Process` in `PoolField`
  drives every pool. Each pool starts at its own point in that cycle, taken from
  its index by a golden-ratio walk (`PoolField.PhaseFor`) — fixed, so the map is
  reproducible, but spread out, so the field shimmers instead of pulsing as one
  sheet. Lighting up is the unshaded albedo brightening — *not* an `OmniLight3D`
  per pool (hundreds of lights in the forward renderer) and not glow
  post-processing (the project has no `WorldEnvironment`). Nothing about the pulse
  touches alpha: a pool is a thing you could fall into, not a tint over the grass.
- **The minimap does not pulse.** Pool geometry is drawn on
  `PurplePool.WorldRenderLayer` (layer 2), the main camera is added to that layer
  in `CreatePools()`, and the minimap camera has it cleared in `CreateMinimap()`;
  the minimap draws flat `ColorRect` markers instead (`UpdateMinimapPoolMarkers`),
  one per pool in `MinimapPoolColor` (the pool's lit colour) and never animated.
  Their size is the pool's true size on the map, floored at
  `MinimapPoolMarkerSize` (3 px) — a 6-unit pool across a 475-unit map is barely
  one pixel, and a marker that cannot be seen is not a marker. Below the cables
  and the node dots, so a node standing in a pool stays readable.
- **Future collision checks** read `GameManager.Pools`: `PoolAt(position)` for a
  point (does this landing spot lie in a pool), `OverlappingPools(centre, radius,
  buffer)` for something with a size of its own (a node, an ammo round).
- `Scenes/structure.tscn` structures are **radius 1.0** (doubled): the mesh, the
  collision shape and the highlight ring (1.4 — kept at 1.4× the node so the ring
  still shows around it). A node is therefore 2 units across, which is what
  `PoolSide` is derived from, and what `Bomb.cs`'s "about one node wide" note
  refers to.

## Camera: centering, panning, minimap

Full documentation lives in **[docs/camera/README.md](docs/camera/README.md)** —
read it before touching anything camera-related. In short:

- Main camera: orthographic, tilted ~45°, looking down toward -Z.
- `MatchCamera.CenterOn()` tweens to `target.XZ + _cameraOffset`; the tween is
  killed whenever the player pans so pan never fights centering.
- `MatchCamera.Pan()` (edge panning) requires window focus, the OS cursor inside
  the window, and no hovered Control.
- Minimap: a runtime-built square `SubViewport` with a top-down camera, rendered
  into a `TextureRect` (bottom-right); every world↔minimap mapping goes through
  `MatchCamera.WorldRect`, which is the map's bounds — change
  `MapDefinition.Size` and the minimap rescales with it.

## Input map (`project.godot`)

- `fire_shot`: Up arrow, W — the arrow keys both aim (Left/Right) and launch
  (Up). In the launch view: hold to charge the power meter, release to fire.
- `aim_left`: A, Left arrow — `aim_right`: D, Right arrow (launch view only).
- `switch_ammo_next`: E — `switch_ammo_previous`: Q. Despite the names (an
  M0.1 leftover), these move the build menu's cursor through the units on the
  page showing, wrapping at both ends.
- `build_page_1`, `build_page_3`, `build_page_7`: the `1`/`3`/`7` keys — switch
  the build menu to that level's page (the pages the roster has a unit at;
  clicking the page buttons does the same).
- `choose_unit`: Enter — build the unit under the menu cursor (it opens the
  launch view). Clicking the unit's button does the same.
- `end_turn`: Space — **skip**: end the turn and carry all unspent energy into
  the next round (docs/parity M1). The menu also has an End turn button.
- `cancel_launch`: Esc — leave the launch view without firing; right-click does
  the same. Nothing is spent either way.
- `dev_overlay`: F1 — opens the developer overlay (see *Tuning*). It is the only
  dev binding here: the panel's own keys (arrows, Enter, R, Esc) are read raw in
  `DevOverlay._Input` rather than through actions, so a dev tool does not consume
  the game's input map.

## Conventions & gotchas

- Tabs for indentation, Allman braces, XML doc comments on public members;
  comments already in the file use `//` and `///` — keep that style.
- Camera/minimap-specific gotchas (SetAnchorsPreset layout, SubViewport input
  isolation, clipped markers, pan-vs-tween invariants) live in
  [docs/camera/README.md](docs/camera/README.md) — see the *Invariants &
  gotchas* section there.

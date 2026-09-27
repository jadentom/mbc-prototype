# AGENTS.md

Agent guide for the **MbcPrototype** repository — a turn-based strategy
prototype built with **Godot 4.5.1 (C#/.NET)**. Read this before editing.

## Project at a glance

- Engine: Godot 4.5.1 stable, GL Compatibility renderer, C# (.NET 8,
  `Godot.NET.Sdk/4.5.1`). Assembly name `MbcPrototype`.
- Single scene: `Scenes/main_scene.tscn` (`res://`) — a 3D world where the
  player and one AI enemy each control a chain of nodes, aim, and launch
  projectiles or bombs, alternating turns. Turns resolve only after every
  in-flight event completes.
- Both sides are the **same kind of thing** (`Combatant`): identical nodes,
  ammo, launch path and defeat rule. The only difference is where the aim and
  power come from (input vs. the AI). See *Combatants & turns* below.
- Ground is a 10000×10000 plane (effectively infinite play space). The play
  area that matters today is around the origin; there is **no real map size
  yet**.

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
├── BaseNode            # the player's starting node (base_node.tscn)
└── UI (CanvasLayer)
    ├── FireStrengthBar (ProgressBar)      # bottom, full width
    ├── Label                              # control hints
    ├── Selector (ColorRect)               # ammo selector highlight
    ├── HBoxContainer (NodeIcon, BombIcon) # ammo icons, bottom-left
    ├── TurnLabel                          # top-right, round + whose move
    ├── DefeatLabel                        # centered, hidden until defeat
    ├── VictoryLabel                       # centered, hidden until victory
    └── RestartLabel                       # centered, hidden until game over
```

Combatants are **not** in the scene: `GameManager.CreateCombatants()` builds
the player's `PlayerCombatant` (adopting the scene's `BaseNode` as its root)
and spawns the enemy's root at runtime — `EnemyStartScreensRight` screen widths
to the right of the player's. Spawned roots are parented under
`GameManager.NodeContainer` (the tree root and the scene root both reject
`AddChild` while the scene is still setting up), as is everything else spawned
during a match — see *Key mechanics & invariants*.

The minimap is **built at runtime** by `GameManager.CreateMinimap()` (a
`SubViewport` + top-down camera + `TextureRect` in the bottom-right corner),
not stored in the scene. See [docs/camera](docs/camera/README.md).

## Core scripts

| File | Role |
| --- | --- |
| `Scripts/Core/GameManager.cs` | Singleton (`GameManager.Instance`), combatant rotation + turn flow, aiming/firing for the player, camera (centering, **edge panning**, **minimap** — see [docs/camera](docs/camera/README.md)). |
| `Scripts/Core/BaseNode.cs` | Chain node: health, highlight ring, cable to parent, health-bar SubViewport sprite, destruction cascade. Knows its owning `OwnerChain`. |
| `Scripts/Core/PlayerCombatant.cs` | The human's combatant: a `NodeChainCombatant` whose aim/charge/fire comes from input in `GameManager`. |
| `Scripts/TurnSystem/Combatant.cs` | Abstract turn participant (team, defeat rule, `BeginTurn`/`EndTurn`). The rotation only ever talks to this. |
| `Scripts/TurnSystem/NodeChainCombatant.cs` | Combatant built from a node chain: node registry, root lookup, defeat check, `Launch()`. |
| `Scripts/Enemies/EnemyCombatant.cs` | The AI enemy: plans one action per turn (alternating node/bomb), random aim/power, fires after `ThinkTime`. |
| `Scripts/Combat/AmmoLauncher.cs` | The single launch path for both sides (aim angle → velocity, ammo scene, `TurnEvent`). |
| `Scripts/Combat/AmmoType.cs` | `AmmoType { Node, Bomb }`. |
| `Scripts/Combat/Projectile.cs` / `Scripts/Combat/Bomb.cs` | Ammo: ballistic bodies that damage `BaseNode`s and resolve a `TurnEvent`. |
| `Scripts/TurnSystem/TurnEvent.cs` | One unit of turn resolution; the turn ends when all pending events resolve. |
| `Shaders/CableShader.gdshader` | Visual cable between chained nodes. |

## Combatants & turns

Authoritative flow, all in `GameManager`:

- `CreateCombatants()` is the **only** place opponents are declared: build the
  combatant, `AddChild` it, add it to `_combatants`, give it a root. Adding a
  second enemy, or a non-node-based one, means deriving from `Combatant` (not
  necessarily `NodeChainCombatant`) and adding it there with another `Team`.
- `StartTurn()` gives the turn to one combatant: a player-controlled one gets
  `IsPlayerTurn = true` and a fresh `SelectNode()`; an AI one gets
  `IsPlayerTurn = false`, the camera centered on its action node, and
  `BeginTurn()`.
- `CompleteTurn()` runs when the pending-event list drains: it ends the acting
  combatant's turn, evaluates the match, and otherwise `AdvanceTurn()`s.
- An AI combatant **must** either register a `TurnEvent` during its turn or
  call `GameManager.EndIdleTurn()`, otherwise the rotation would wait forever.
- Win/lose: `EvaluateGameOver()` ends the match when a whole `Team` has no
  combatant left that can act — `VICTORY` if only the player's team is left,
  `DEFEAT` otherwise. Because destroying a root cascades through its whole
  chain, "the enemy's root node was destroyed" is exactly this check.
- `CurrentTurn` counts **rounds** (it advances when the rotation wraps), and
  the turn label shows the round plus whose move it is.
- Game over: `_UnhandledInput` restarts the match on any key/click by
  reloading the scene (deferred by a frame, so the reload never runs from
  inside a node it is about to free).

Team/targeting details:

- `Combatant.Team` (player 0, enemy 1) decides who may target whom; the AI
  picks the nearest hostile node and fires from its own node closest to it, so
  its chain creeps toward the player.
- `BaseNode.OwnerChain` is set **before** the node enters the tree (by the
  spawning combatant, or by the projectile that deployed it) so `_Ready` can
  register it with the right chain. `SelectNode()` only accepts the player's
  own chain, so clicks can't hijack the enemy's.
- A node deployed by a projectile whose creator died mid-flight spawns
  **unowned**: it belongs to no combatant and keeps no defeated chain alive.

## Key mechanics & invariants

- **Turn system**: the first `RegisterTurnEvent` commits the turn
  (`IsPlayerTurn=false`, `IsResolvingTurn=true`); control returns in
  `CompleteTurn()` when the pending-event list drains. `BaseNode.Destroy()`
  registers a `TurnEvent` resolved in `_ExitTree` so destruction cascades
  finish before the turn ends.
- **Node destruction**: `IsDestroyed` (set immediately) vs
  `IsInstanceValid` (still true until `QueueFree` takes effect at end of
  frame). Always check the flag, never just instance validity.
- **Selection**: `SelectNode()` only accepts a live node of the player's own
  chain, re-centers the camera via a tween, and manages the highlight. When
  the selection dies, control reverts to the player's chain root (a node with
  no valid parent).
- **Aiming**: `aim_left`/`aim_right` rotate `_currentAimAngle` (A/D + arrow
  keys); `fire_shot` charges a power bar; release launches the current ammo.
  The AI drives the same `_currentAimAngle`-equivalent through
  `ShowAimPreview()` and `AmmoLauncher`, so both sides shoot identically.
- **Enemy tuning** (all exported on `GameManager`): `EnemyStartScreensRight`
  (default 2), `EnemyThinkTime`, `EnemyAimSpreadDegrees` (180 = fully random),
  `EnemyMinPower`/`EnemyMaxPower`, `CenterCameraOnActingCombatant`.
- **Nothing spawned during a match is parented to the tree root.** Chain roots,
  ammo in flight, explosions and the nodes ammo deploys all go under
  `GameManager.SpawnContainer` (`NodeContainer`), and the player's starting
  node is looked up through `GameManager.SceneRoot` — both *inside* the scene.
  Restarting a finished match reloads the scene, and a reload frees the scene
  alone: a node left under the tree root outlives the match, comes back as a
  stale node nobody can select (its chain is gone), and — being the first
  `BaseNode` in the tree — would be adopted as the *next* match's player root,
  which then spawns the enemy and the camera offset off the wrong place.

## Camera: centering, panning, minimap

Full documentation lives in **[docs/camera/README.md](docs/camera/README.md)** —
read it before touching anything camera-related. In short:

- Main camera: orthographic, tilted ~45°, looking down toward -Z.
- `CenterCameraOn()` tweens to `target.XZ + _cameraOffset`; the tween is
  killed whenever the player pans so pan never fights centering.
- `HandleCameraPanning()` (edge panning) requires window focus, the OS cursor
  inside the window, and no hovered Control.
- Minimap: a runtime-built square `SubViewport` with a top-down camera,
  rendered into a `TextureRect` (bottom-right); every world↔minimap mapping
  goes through `GetMinimapWorldRect()` (the scaling seam for a real map).

## Input map (`project.godot`)

- `fire_shot`: Space, W
- `aim_left`: A, Left arrow
- `aim_right`: D, Right arrow
- `switch_ammo_next`: E — `switch_ammo_previous`: Q

## Conventions & gotchas

- Tabs for indentation, Allman braces, XML doc comments on public members;
  comments already in the file use `//` and `///` — keep that style.
- Camera/minimap-specific gotchas (SetAnchorsPreset layout, SubViewport input
  isolation, clipped markers, pan-vs-tween invariants) live in
  [docs/camera/README.md](docs/camera/README.md) — see the *Invariants &
  gotchas* section there.

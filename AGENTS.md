# AGENTS.md

Agent guide for the **MbcPrototype** repository — a turn-based strategy
prototype built with **Godot 4.5.1 (C#/.NET)**. Read this before editing.

## Project at a glance

- Engine: Godot 4.5.1 stable, GL Compatibility renderer, C# (.NET 8,
  `Godot.NET.Sdk/4.5.1`). Assembly name `MbcPrototype`.
- Single scene: `Scenes/main_scene.tscn` (`res://`) — a 3D world where the
  player controls a chain of nodes, aims with arrow keys, and launches
  projectiles or bombs. Turns resolve only after every in-flight event
  completes.
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
├── BaseNode            # the starting node of the chain (base_node.tscn)
└── UI (CanvasLayer)
    ├── FireStrengthBar (ProgressBar)      # bottom, full width
    ├── Label                              # control hints
    ├── Selector (ColorRect)               # ammo selector highlight
    ├── HBoxContainer (NodeIcon, BombIcon) # ammo icons, bottom-left
    ├── TurnLabel                          # top-right
    └── DefeatLabel                        # centered, hidden until defeat
```

The minimap is **built at runtime** by `GameManager.CreateMinimap()` (a
`SubViewport` + top-down camera + `TextureRect` in the bottom-right corner),
not stored in the scene. See [docs/camera](docs/camera/README.md).

## Core scripts

| File | Role |
| --- | --- |
| `Scripts/Core/GameManager.cs` | Singleton (`GameManager.Instance`), turn flow, aiming/firing, camera (centering, **edge panning**, **minimap** — see [docs/camera](docs/camera/README.md)). |
| `Scripts/Core/BaseNode.cs` | Chain node: health, highlight ring, cable to parent, health-bar SubViewport sprite, destruction cascade. |
| `Scripts/Combat/Projectile.cs` / `Scripts/Combat/Bomb.cs` | Ammo: ballistic bodies that damage `BaseNode`s and resolve a `TurnEvent`. |
| `Scripts/TurnSystem/TurnEvent.cs` | One unit of turn resolution; the turn ends when all pending events resolve. |
| `Shaders/CableShader.gdshader` | Visual cable between chained nodes. |

## Key mechanics & invariants

- **Turn system**: the first `RegisterTurnEvent` commits the turn
  (`IsPlayerTurn=false`, `IsResolvingTurn=true`); control returns in
  `CompleteTurn()` when the pending-event list drains. `BaseNode.Destroy()`
  registers a `TurnEvent` resolved in `_ExitTree` so destruction cascades
  finish before the turn ends.
- **Node destruction**: `IsDestroyed` (set immediately) vs
  `IsInstanceValid` (still true until `QueueFree` takes effect at end of
  frame). Always check the flag, never just instance validity.
- **Selection**: `SelectNode()` re-centers the camera via a tween; the
  selected node's highlight is managed there. Control reverts to the highest
  remaining node (a root with no valid parent) when the selection dies, or
  triggers defeat when nothing remains.
- **Aiming**: `aim_left`/`aim_right` rotate `_currentAimAngle` (A/D + arrow
  keys); `fire_shot` charges a power bar; release launches the current ammo.

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

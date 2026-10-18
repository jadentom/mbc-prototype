# MbcPrototype

A turn-based strategy prototype built with **Godot 4.5.1 (C#/.NET)**.

In a 3D world, the player controls a chain of structures — cords hanging from a
starting Hub — and builds by launching units out of a Hub. An AI enemy, the same
kind of chain two screens to the right, takes its own turns.

**A turn is an energy budget.** You start with 7 energy (out of a maximum of 35)
and every unit costs its level — 1, 3 or 7. Pick a unit from the build menu, aim,
charge and fire; you can keep building until the energy runs out, or end the turn
with Space and carry what is left into the next round. A round is one turn from
each seat; at its end the main Hub pays 7 and each Harvester pays 3 (on an energy
pool) or 1 (off one), and the player order rotates left. The human's turn has a
60-second clock; when it runs out the turn ends. A shot resolves only after every
in-flight event (shot, explosion, destruction cascade) completes, then the same
seat — or the next one — takes over.

Destroy the enemy's **main Hub** to win: a Hub's destruction cascades down its
cords through everything it anchors, so the match ends the moment one side runs
out of structures. Any key or click on the victory/defeat screen restarts.

The map is **data** too: `Resources/Maps/development_map.tres` holds the play
area the minimap shows, where the two sides start, and how big the energy pools
are and how much of the ground they cover. The economy's numbers — starting
energy, the cap, the Hub's and the Harvester's incomes, the clock — are the
match's `MatchRules`. The three units are data as well — a `UnitDefinition`
resource per unit in a `UnitCatalog`, so their numbers are edited in the editor
rather than in code: the **Hub** (the only launcher, and the only structure that
generates energy), the **Harvester** (energy every round, more of it while it
stands on a pool) and the **Bomb**. The Bomb does 3 damage to a 5-hit-point Hub,
so two direct hits destroy one, and everything within 1.5 world units of where it
lands takes those 3 while the ring out to 3 units takes half (rounded down, so 1).
The blast draws expanding spheres and leaves a marker on the ground showing where
those radii were, and every health bar is divided into one segment per point of
health.

## Requirements

- Godot 4.5.1 stable (`.NET`/mono build), GL Compatibility renderer
- .NET 8 SDK (`Godot.NET.Sdk/4.5.1`, assembly name `MbcPrototype`)

## Build & run

```sh
dotnet build MbcPrototype.csproj
```

Open the project in Godot (or launch the editor binary with `--path <repo>`)
and run `Scenes/main_scene.tscn` (`res://`).

## Controls

| Action | Input |
| --- | --- |
| Build page | `1` / `3` / `7` (or click the page buttons) |
| Pick a unit on the page | E (next), Q (previous) |
| Choose it and aim | Enter, or click the unit's button |
| Aim left / right | A / D, Left / Right arrow |
| Fire (charge & release) | Up arrow, W |
| Cancel back to the menu | Esc, or right-click |
| End turn / skip | Space (or the End turn button) |
| Select the Hub to build from | Left-click one of your structures |
| Pan camera | Move mouse to a screen edge |
| Jump camera | Click the minimap |
| Restart (game over) | Any key or click |

## Project structure

```
Scenes/          Godot scene files (main_scene, structure, ammo, health bar)
Scripts/
  Core/          GameManager (the player's build/launch input and the turn clock),
                 MatchSetup (the match and its combatants), Structure (a unit on
                 the map), Cord (the link between two of them), PlayerCombatant
  Presentation/  BuildMenu (the build menu and launch view), MatchCamera
                 (centering, panning, minimap)
  World/         MapDefinition, MatchWorld, PoolField, PurplePool, TerrainState
  Units/         UnitDefinition, UnitCatalog, UnitTraits — the roster, as data
  Rules/         MatchRules — the match's global numbers, the energy economy
                 and the turn clock
  Combat/        Projectile, Bomb (+ BlastVisual), Explosion, AmmoLauncher,
                 ILaunchedAmmo (+ AmmoLaunch)
  TurnSystem/    TurnEvent, TurnController (the rotation and the rounds),
                 Combatant (energy purse), NodeChainCombatant
  Enemies/       EnemyCombatant (the AI), AiProfile (how it plays)
  Simulation/    MatchCommand, CommandLog (the match's input)
  Testing/       MatchHarness, TuningSurface, DevOverlay
Resources/       Content: Resources/Units/*.tres and Resources/Rules/*.tres
Shaders/         CableShader.gdshader
Assets/          Textures
docs/            Topic documentation (camera, parity plan, …)
```

## Tests

`Scenes/harness.tscn` plays a match with nobody at the keyboard — the harness
takes the human's seat, the AI plays itself — and its state hash is how a change
is checked:

```sh
dotnet build MbcPrototype.csproj
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --turns=10 --seed=7 --record=/tmp/m.json
godot --headless --fixed-fps 60 --path . Scenes/harness.tscn -- --replay=/tmp/m.json --expect=<hash>
```

The second command fails (non-zero exit) unless the replay lands in exactly the
state the recording did. Change a number in a `.tres` resource, replay the same
log, and a different hash is the proof the change reached the simulation.

## Documentation

- `AGENTS.md` — agent guide for contributors (read before editing)
- [`docs/parity/README.md`](docs/parity/README.md) — the plan for this prototype
  as the base for a spiritual sequel, and what has landed so far
- [`docs/camera/README.md`](docs/camera/README.md) — camera: centering, edge
  panning, minimap

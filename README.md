# MbcPrototype

A turn-based strategy prototype built with **Godot 4.5.1 (C#/.NET)**.

In a 3D world, the player controls a chain of structures — cords hanging from a
starting Hub — aims with the arrow keys, and launches units from a roster. An AI
enemy, the same kind of chain two screens to the right, takes every other turn.
Turns resolve only after every in-flight event (shot, explosion, destruction
cascade) completes, then the turn passes to the other side.

Destroy the enemy's root to win: a root's destruction cascades down its cords
through everything it anchors, so the match ends the moment one side runs out of
structures. Any key or click on the victory/defeat screen restarts.

The map is **data** too: `Resources/Maps/development_map.tres` holds the play
area the minimap shows, where the two sides start, and how big the energy pools
are and how much of the ground they cover. There are two units, likewise data — a
`UnitDefinition` resource per unit in a `UnitCatalog`, so their numbers are
edited in the editor rather than in code. The Bomb is the weapon — it does 3 damage to a 5-hit-point Hub, so two
direct hits destroy one, and everything within 1.5 world units of where it lands
takes those 3 while the ring out to 3 units takes half (rounded down, so 1). The
blast draws expanding spheres and leaves a marker on the ground showing where
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
| Fire (charge & release) | Up arrow, W |
| Aim left / right | A / D, Left / Right arrow |
| Switch unit | E (next), Q (previous) |
| Select structure | Left-click one of your structures |
| Pan camera | Move mouse to a screen edge |
| Jump camera | Click the minimap |
| Restart (game over) | Any key or click |

## Project structure

```
Scenes/          Godot scene files (main_scene, structure, ammo, health bar)
Scripts/
  Core/          GameManager (turn rotation, aiming/firing, camera), Structure
                 (a unit on the map), Cord (the link between two of them),
                 PlayerCombatant
  Units/         UnitDefinition, UnitCatalog, UnitTraits — the roster, as data
  Rules/         MatchRules — the match's global numbers
  Combat/        Projectile, Bomb (+ BlastVisual), Explosion, AmmoLauncher,
                 ILaunchedAmmo (+ AmmoLaunch)
  TurnSystem/    TurnEvent, Combatant, NodeChainCombatant
  Enemies/       EnemyCombatant (the AI)
Resources/       Content: Resources/Units/*.tres and Resources/Rules/*.tres
Shaders/         CableShader.gdshader
Assets/          Textures
docs/            Topic documentation (camera, parity plan, …)
```

## Documentation

- `AGENTS.md` — agent guide for contributors (read before editing)
- [`docs/parity/README.md`](docs/parity/README.md) — the plan for this prototype
  as the base for a spiritual sequel, and what has landed so far
- [`docs/camera/README.md`](docs/camera/README.md) — camera: centering, edge
  panning, minimap

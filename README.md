# MbcPrototype

A turn-based strategy prototype built with **Godot 4.5.1 (C#/.NET)**.

In a 3D world, the player controls a chain of nodes, aims with the arrow keys,
and launches projectiles or bombs. An AI enemy — the same kind of chain, two
screens to the right — takes every other turn. Turns resolve only after every
in-flight event (shot, explosion, destruction cascade) completes, then the turn
passes to the other side.

Destroy the enemy's root node to win: a root's destruction cascades through its
whole chain, so the match ends the moment one side runs out of nodes. Any key
or click on the victory/defeat screen restarts.

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
| Fire (charge & release) | Space, W |
| Aim left / right | A / D, Left / Right arrow |
| Switch ammo | E (next), Q (previous) |
| Select node | Left-click one of your nodes |
| Pan camera | Move mouse to a screen edge |
| Jump camera | Click the minimap |
| Restart (game over) | Any key or click |

## Project structure

```
Scenes/          Godot scene files (main_scene, base_node, ammo, health bar)
Scripts/
  Core/          GameManager (turn rotation, aiming/firing, camera), BaseNode,
                 PlayerCombatant
  Combat/        Projectile, Bomb, Explosion, AmmoLauncher, AmmoType
  TurnSystem/    TurnEvent, Combatant, NodeChainCombatant
  Enemies/       EnemyCombatant (the AI)
Shaders/         CableShader.gdshader
Assets/          Textures
docs/            Topic documentation (camera, …)
```

## Documentation

- `AGENTS.md` — agent guide for contributors (read before editing)
- [`docs/camera/README.md`](docs/camera/README.md) — camera: centering, edge
  panning, minimap

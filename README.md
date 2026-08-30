# MbcPrototype

A turn-based strategy prototype built with **Godot 4.5.1 (C#/.NET)**.

In a 3D world, the player controls a chain of nodes, aims with the arrow keys,
and launches projectiles or bombs. Turns resolve only after every in-flight
event (shot, explosion, destruction cascade) completes, then control returns
to the player.

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
| Select node | Left-click a node |
| Pan camera | Move mouse to a screen edge |
| Jump camera | Click the minimap |

## Project structure

```
Scenes/          Godot scene files (main_scene, base_node, ammo, health bar)
Scripts/
  Core/          GameManager (turn flow, aiming/firing, camera), BaseNode
  Combat/        Projectile, Bomb, Explosion
  TurnSystem/    TurnEvent — one unit of turn resolution
Shaders/         CableShader.gdshader
Assets/          Textures
docs/            Topic documentation (camera, …)
```

## Documentation

- `AGENTS.md` — agent guide for contributors (read before editing)
- [`docs/camera/README.md`](docs/camera/README.md) — camera: centering, edge
  panning, minimap

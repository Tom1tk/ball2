# Ball2 — Godot C# Rewrite

A port of [Ball2](https://github.com/Tom1tk/ball2) from Unity to **Godot 4.7 .NET**, keeping the
gameplay code in **C#**. The original Unity project remains on `main`; this branch (`godot-rewrite`)
contains the Godot project at the repository root, mirroring how `bevy-rewrite` is laid out.

> **Status:** playable vertical slice. Movement, camera, dual grappling hooks, momentum combat,
> lock-on, damage states, enemy AI, HUD, pause/lose screens and the main menu are ported.
> Navigation-mesh enemy pathfinding, gameplay integration tests, pickups/win flow, audio and polish
> remain (see `MIGRATION_PLAN.md` §6).

## Requirements

| Tool | Version |
|---|---|
| Godot | 4.7.x **.NET / Mono** build |
| .NET SDK | 8.0 |

The standard (non-.NET) Godot build cannot load this project. On macOS the .NET build is a separate
app bundle (for example `Godot_mono.app`).

## Run

Open `project.godot` in the Godot .NET editor and press **F5**, or from a terminal:

```bash
godot --path .                 # uses the menu scene
godot --path . res://scenes/main.tscn
```

If `godot` is not on your PATH, point it at the Godot .NET executable directly.

## Build and test

```bash
dotnet build Ball2Godot.csproj
dotnet test tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj
```

The engine-agnostic rules live in `Ball2.Core` and are covered by **37 NUnit tests**
(14 combat + 22 lock-on + 1 canary). Godot-facing scripts are adapters around those rules.

## Controls

| Input | Action |
|---|---|
| `W A S D` | Roll / steer |
| `Space` | Hold to charge boost, release to launch |
| `Q` / `E` | Dodge left / right |
| `LMB` / `RMB` | Fire left / right grapple hook |
| `Shift` | Reel in while hooked |
| `Esc` | Pause |
| `R` | Restart level |

Charging a boost near an enemy acquires a soft lock; the lock commits when the charge starts.

## Course

The active course is transcribed from `Assets/Scenes/Test2.unity` on `main`: a 500 × 500 m floor,
41 box colliders including the tall tower course, and a single enemy. It lives in
`scenes/arena_test2.tscn` and is instanced by `scenes/main.tscn`. `Test.unity` is an earlier
100 × 100 m prototype and is not used by the port.

Tuning values come from the Unity project where possible: gravity is `-40`, the player ball uses
`gravityScale = 0.8`, and rigid bodies use angular damping `20`.

## Project structure

```
project.godot              Godot project (input map, layers, main scene)
Ball2Godot.csproj          Godot C# game assembly
Ball2.Core/                Engine-agnostic rules (System.Numerics, no Godot references)
  Combat/                  CombatResolver, LockOnResolver and their config/result types
scripts/Gameplay/          Balls, camera, grapple, AI, health and resolver adapters
scripts/UI/                HUD/pause/lose and main-menu scripts
scenes/                    main, arena_test2, player, enemy, hook, HUD and menu scenes
assets/                    Art and audio copied from the Unity project (Git LFS)
docs/                      Original design/spec documents (Unity-era, kept for reference)
tests/Ball2.Core.Tests/    NUnit test project
```

## Assets

The Unity art (ball damage textures, crosshairs, lock indicator and prototype grid textures) was
copied under `assets/` and is stored through Git LFS. The original Unity `HamsterSprite` GUID does
not resolve to a file in the repository, so `assets/sprites/hamster.svg` is a deliberate placeholder
that preserves the hamster-inside-the-ball look. Ball materials are translucent so the sprite stays
visible.

## Branches

- `main` — original Unity 6000.4 project.
- `bevy-rewrite` — earlier engine port.
- `godot-rewrite` — this Godot .NET port.

See `MIGRATION_PLAN.md` for the Unity-to-Godot mapping, parity status, test strategy and remaining work.

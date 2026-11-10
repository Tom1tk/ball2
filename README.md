# Ball2 — Godot C# Rewrite

A port of [Ball2](https://github.com/Tom1tk/ball2) from Unity to **Godot 4.7 .NET**, keeping the
gameplay code in **C#**. The original Unity project remains on `main`; this branch (`godot-rewrite`)
contains the Godot project at the repository root, mirroring how `bevy-rewrite` is laid out.

> **Status:** feature-complete migration of the Unity vertical slice. Movement, camera, dual grappling
> hooks, momentum combat, lock-on, damage states, navigation-mesh enemy AI, HUD, pause/controls/lose
> screens, main menu, audio and input remapping are ported. Core rules and gameplay are covered by
> automated tests; CI builds and tests the branch (see `MIGRATION_PLAN.md`).

## Requirements

| Tool | Version |
|---|---|
| Godot | 4.7.x **.NET / Mono** build |
| .NET SDK | 8.0 |
| Git LFS | 3.x |

The standard (non-.NET) Godot build cannot load this project. On macOS the .NET build is a separate
app bundle (for example `Godot_mono.app`).

## First clone

Art, audio and the sprite set are stored in **Git LFS**. Install LFS before checking out, or the PNG
and WAV files will be small pointer text files and the game will open with missing textures/audio.

```bash
git lfs install
git clone https://github.com/Tom1tk/ball2.git
cd ball2
git switch godot-rewrite
git lfs pull
./scripts/setup.sh        # LFS hooks + pulls assets + restores .NET packages
```

`scripts/setup.sh` fails loudly if an asset is still an LFS pointer.

## Run

Open `project.godot` in the Godot .NET editor and press **F5**, or from a terminal:

```bash
godot --path .                 # uses the menu scene
godot --path . res://scenes/main.tscn
```

If `godot` is not on your PATH, point it at the Godot .NET executable directly (`godot-mono` on the
development Mac).

## Build and test

```bash
dotnet build Ball2Godot.csproj
dotnet test tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj          # 37 core rules tests

# Headless gameplay integration tests (47 checks, exit code 0 = pass)
godot --headless --path . res://tests/GameplayTests.tscn

# Smoke test the main scene
godot --headless --path . res://scenes/main.tscn --quit-after 120
```

The engine-agnostic rules live in `Ball2.Core` and are covered by **37 NUnit tests**
(14 combat + 22 lock-on + 1 canary). The gameplay suite loads the real `main.tscn` and checks
navigation routing, damage textures, lock-on acquisition/break, the grapple state machine,
ball-to-ball contact damage, pause/resume, audio/rebinding and death/despawn flow.

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

Open **Pause → Controls** to rebind any action to a key or mouse button. Bindings are stored in
`user://input_bindings.cfg` and survive restarts.

Charging a boost near an enemy acquires a soft lock; the lock commits when the charge starts.

## Course

The active course is transcribed from `Assets/Scenes/Test2.unity` on `main`: a 500 × 500 m floor,
41 box colliders including the tall tower course, and a single enemy. It lives in
`scenes/arena_test2.tscn` and is instanced by `scenes/main.tscn` inside a `NavigationRegion3D` with a
pre-baked navigation mesh (`scenes/arena_test2_navmesh.tres`).

Tuning values come from the Unity project where possible: gravity is `-40`, the player ball uses
`gravityScale = 0.8`, and rigid bodies use angular damping `20`. Spawn heights are placed at the
Godot ball's resting height (1.05 m) so the initial drop does not trigger wall damage.

The older 100 × 100 m `Test.unity` prototype is available as an optional comparison scene:

```bash
godot --path . res://scenes/main_test.tscn    # legacy arena, two enemies
```

It uses `scenes/arena_test.tscn` and its own baked navmesh. `Test2` remains the active course.

## Project structure

```
project.godot              Godot project (autoloads, input map, layers, main scene)
Ball2Godot.csproj          Godot C# game assembly
Ball2.Core/                Engine-agnostic rules (System.Numerics, no Godot references)
  Combat/                  CombatResolver, LockOnResolver and their config/result types
scripts/Gameplay/          Balls, camera, grapple, navigation AI, health and resolver adapters
scripts/UI/                HUD/pause/controls/lose and main-menu scripts
scripts/Audio/             Sound pool autoload
scripts/Tests/             Headless gameplay integration test runner
scenes/                    main, arenas, navmeshes, player, enemy, hook, HUD and menu scenes
assets/textures|sprites/   Unity art plus the original Godot hamster SVG (Git LFS)
assets/audio/              Generated boost/dodge/hit/grapple/death/UI sounds
docs/                      Original design/spec documents (Unity-era, kept for reference)
tests/Ball2.Core.Tests/    NUnit test project
tests/GameplayTests.tscn   Headless gameplay suite
export_presets.cfg         Linux, Windows and macOS export presets
.github/workflows/         Godot CI (LFS checkout, import, build, tests, smoke)
```

## Assets

The Unity art (ball damage textures, crosshairs, lock indicator and prototype grid textures) was
copied under `assets/` and is stored through Git LFS. The original Unity `HamsterSprite` GUID does
not resolve to a file in the repository, so `assets/sprites/hamster.svg` is an original Godot
replacement that preserves the hamster-inside-the-ball look. Ball materials use alpha `0.68` with a
rim light so the sprite stays visible.

Audio is a small generated WAV set (no audio sources existed in the Unity project):
boost charge/release, dodge, light/heavy hit, grapple fire/hook, death and UI click.

## Export

`export_presets.cfg` defines Linux, Windows and macOS presets. Install the matching Godot 4.7.2 .NET
export templates, then use **Project → Export** or:

```bash
godot --headless --path . --export-release "Linux/X11" build/linux/ball2.x86_64
```

The CI workflow builds and tests the project; exporting requires templates, which are not downloaded
by default.

## Design reference

The original Unity-era spec is kept in `docs/BALL2_SPEC.md` as design reference. It is not
authoritative for this branch where it conflicts with the Godot implementation. Read
`docs/GODOT_ADDENDUM.md` for what carries over, what is outdated, and the Godot working rules.

## Performance

On the development M1 Pro the Test2 scene holds the 120 Hz vsync cap (8.33 ms/frame) with 41 boxes,
shadows, the procedural sky and one navigating enemy. The directional shadow range is capped at
250 m. The earlier “incredibly laggy” run happened while the movie writer was competing for the GPU
and on a first-run shader compile; direct runs do not reproduce it.

## Branches

- `main` — original Unity 6000.4 project.
- `bevy-rewrite` — earlier engine port.
- `godot-rewrite` — this Godot .NET port.

See `MIGRATION_PLAN.md` for the Unity-to-Godot mapping, parity status, test strategy and remaining work.

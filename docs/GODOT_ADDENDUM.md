# Ball2 — Godot Branch Addendum

This branch is the **Godot 4.7 .NET / C# port** of Ball2. The documents in this directory are the
original Unity-era design, task and progress records. They are kept as **design reference**, not as
the current engine contract.

Where the Unity documents and the Godot branch disagree, the Godot branch wins:

1. `../README.md` — how to run, build and test.
2. `../MIGRATION_PLAN.md` — Unity → Godot mapping, parity status, remaining work.
3. This addendum — design intent that carries over and known differences.
4. `BALL2_SPEC.md`, `BALL2_TASKS.md`, `AGENTS.md`, `PROGRESS.md` — historical Unity reference.

## Design intent that still applies

- Momentum-differential combat: relative speed along the contact normal, mass-weighted damage and
  knockback, perfect-hit window.
- Dual independent grapple hooks with spring/damper rope, reel and swing assists.
- Soft lock-on with acquisition, tracking, break conditions and commit-on-boost-charge.
- Reduced-gravity ball movement, charged boost, left/right dodge and air strafe.
- HUD, pause, lose screen and main menu.

## Current Godot scope

- **Active course:** `Test2.unity` → `scenes/arena_test2.tscn` (500 × 500 m, 41 box colliders,
  tall tower course, one enemy, no ceiling). `Test.unity` is the older 100 × 100 prototype and is
  not used.
- **Tuning from the Unity project:** gravity `-40`, player `gravityScale = 0.8`, angular damping
  `20`, camera FOV `75`, clip planes `0.1..5000`, sensitivity `0.1`, pitch `-30..60`, warm sun at
  `1.3`.
- **Optional legacy course:** `Test.unity` → `scenes/arena_test.tscn` / `scenes/main_test.tscn`
  (100 × 100 m, two enemies). Test2 remains the active course.
- **Enemy navigation:** pre-baked `scenes/arena_test2_navmesh.tres` (357 polygons, 1.1 m agent radius)
  inside a `NavigationRegion3D`; `EnemyAI` uses `NavigationAgent3D` with direct steering as fallback.
- **Removed:** strawberry counter and win screen. They were remnants of an earlier iteration; do not
  reintroduce them without an explicit request.
- **Audio:** generated WAV set under `assets/audio/` with a pooled `AudioManager` autoload.
- **Controls:** Pause → Controls lets the player rebind keys/mouse buttons; bindings are saved to
  `user://input_bindings.cfg`.
- **Tests:** 37 NUnit core tests plus `tests/GameplayTests.tscn`, a 47-check headless suite.
- **Build/CI:** `export_presets.cfg` for Linux/Windows/macOS and `.github/workflows/godot.yml` for
  LFS checkout, import, build, tests and smoke checks.

## Godot working rules

- **C# only.** Do not ship GDScript gameplay.
- `Ball2.Core/` stays engine-agnostic and deterministic: no `Godot` or `UnityEngine` types, no
  frame/time/RNG reads. Use `System.Numerics`.
- Godot-facing code is an adapter in `scripts/Gameplay/` or `scripts/UI/`.
- Expose tuning as `[Export]` fields or core config types; do not hard-code feel numbers.
- Use the **Godot .NET** build, not standard Godot. Build with `dotnet build Ball2Godot.csproj`,
  test with `dotnet test tests/Ball2.Core.Tests/...` plus
  `godot --headless --path . res://tests/GameplayTests.tscn`, and smoke-test scenes headlessly.
- Assets are Git LFS-backed. Run `git lfs install && git lfs pull` (or `scripts/setup.sh`) after a
  fresh clone; do not commit LFS pointer files as if they were art.
- Keep `MIGRATION_PLAN.md` and this addendum current when scope or parity changes.

## Unity-era material that is now historical

- Unity component names, asset paths, `MonoBehaviour`/`FixedUpdate` patterns and `Assets/` layout.
- `Test.unity` as the course; the active course is Test2 (the legacy scene is optional).
- NavMeshAgent-based enemy movement; Godot uses a baked `NavigationMesh` + `NavigationAgent3D`.
- `Tools/run-tests.sh` and the Unity Test Framework (use `dotnet test` and the Godot headless suite).
- The Unity lane rules in `AGENTS.md` and the Fishnet/netcode section of the spec are design
  reference only; they are not implemented in the Godot branch.

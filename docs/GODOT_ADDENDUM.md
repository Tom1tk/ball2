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
- **Removed:** strawberry counter and win screen. They were remnants of an earlier iteration; do not
  reintroduce them without an explicit request.
- **Not yet ported:** NavMesh enemy pathfinding, gameplay integration tests, audio, input remapping,
  export presets/CI, optional old `Test.unity` arena.

## Godot working rules

- **C# only.** Do not ship GDScript gameplay.
- `Ball2.Core/` stays engine-agnostic and deterministic: no `Godot` or `UnityEngine` types, no
  frame/time/RNG reads. Use `System.Numerics`.
- Godot-facing code is an adapter in `scripts/Gameplay/` or `scripts/UI/`.
- Expose tuning as `[Export]` fields or core config types; do not hard-code feel numbers.
- Use the **Godot .NET** build, not standard Godot. Build with `dotnet build Ball2Godot.csproj`,
  test with `dotnet test tests/Ball2.Core.Tests/...`, and smoke-test scenes headlessly.
- Keep `MIGRATION_PLAN.md` and this addendum current when scope or parity changes.

## Unity-era material that is now historical

- Unity component names, asset paths, `MonoBehaviour`/`FixedUpdate` patterns and `Assets/` layout.
- `Test.unity` as the course; the active course is Test2.
- NavMeshAgent-based enemy movement (Godot navigation is still to be added).
- `Tools/run-tests.sh` and the Unity Test Framework (use `dotnet test`).
- The Unity lane rules in `AGENTS.md` and the Fishnet/netcode section of the spec are design
  reference only; they are not implemented in the Godot branch.

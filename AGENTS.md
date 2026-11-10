# AGENTS.md — Ball2 Godot branch

This branch is the **Godot 4.7 .NET / C#** port of Ball2. Before changing anything, read:

- `README.md` — run, build and test commands.
- `MIGRATION_PLAN.md` — Unity → Godot mapping, parity status and remaining work.
- `docs/GODOT_ADDENDUM.md` — what carries over from the Unity design and what has changed.

## Rules

- **C# only.** Do not add GDScript gameplay.
- Keep `Ball2.Core/` engine-agnostic and deterministic: no `Godot`/`UnityEngine` types, no
  frame-time or unseeded RNG reads. Use `System.Numerics`.
- Put Godot-facing code in `scripts/Gameplay/` or `scripts/UI/` as adapters around the core rules.
- Expose tuning as `[Export]` fields or core config types; don't hard-code feel values.
- Use the **Godot .NET** build. Verify with:
  - `dotnet build Ball2Godot.csproj`
  - `dotnet test tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj`
  - `godot --headless --path . res://tests/GameplayTests.tscn` (47 gameplay checks)
  - headless scene smoke tests (`godot --headless --path . res://scenes/main.tscn --quit-after N`)
- The active course is `scenes/arena_test2.tscn`, transcribed from `Assets/Scenes/Test2.unity` on
  `main`, with its baked navmesh. `Test.unity` is available as the optional `main_test.tscn` comparison
  scene.
- Assets live in Git LFS. Run `git lfs install && git lfs pull` (or `scripts/setup.sh`) after a fresh
  clone; never commit a pointer file in place of art.
- Strawberries and the win screen were intentionally removed. Do not reintroduce them without an
  explicit request.

## Documentation authority

`docs/BALL2_SPEC.md`, `docs/BALL2_TASKS.md`, `docs/AGENTS.md` and `docs/PROGRESS.md` are **Unity-era
reference material**. Use them for design intent and numbers, but do not follow their Unity-specific
engine instructions, file paths, lane rules or test commands literally. When they conflict with the
Godot branch, `README.md`, `MIGRATION_PLAN.md` and `docs/GODOT_ADDENDUM.md` take precedence.

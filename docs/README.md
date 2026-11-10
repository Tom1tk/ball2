# Docs on this branch

These are the original Ball2 design, task and progress documents, copied from `Assets/Docs/` on the
Unity `main` branch. They describe the Unity implementation and are kept as **reference** for design
intent, numbers and acceptance criteria. They are not authoritative for the Godot branch where they
conflict with the current project.

Start with **`GODOT_ADDENDUM.md`**, which records what carries over from the Unity design and what has
changed. For the current project:

- `../README.md` — how to run, build and test the Godot project.
- `../MIGRATION_PLAN.md` — Unity-to-Godot mapping, parity status and remaining work.
- `GODOT_ADDENDUM.md` — current design scope, tuning provenance and Godot working rules.

The active course in this branch is transcribed from `Assets/Scenes/Test2.unity` (500 × 500 m, 41
colliders, tall tower course); `Test.unity` is available as the optional 100 × 100 comparison scene.
Where the branch intentionally differs from the Unity docs (translucent ball materials, original
hamster SVG, baked Godot navmesh, generated audio, removed strawberry/win-screen remnants), the
difference is recorded in `../MIGRATION_PLAN.md` §3 and `GODOT_ADDENDUM.md`.

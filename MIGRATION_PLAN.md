# Ball2 — Unity → Godot Migration Plan

**Source:** `Tom1tk/ball2`, branch `main`, Unity 6000.4.10f1 (Built-in Render Pipeline).
**Target:** `Tom1tk/ball2`, branch `godot-rewrite`, Godot 4.7.x .NET with C# and .NET 8.
**Approach:** a parallel branch, like `bevy-rewrite`, rather than an in-place conversion. The Godot
project sits at the branch root; the Unity project stays untouched on `main`.

---

## 1. Goals and constraints

1. **Keep C#.** Gameplay stays in C#; no GDScript in the shipping game.
2. **Preserve behaviour.** The Unity spec in `docs/BALL2_SPEC.md` is the design reference for intent
   and numbers. Where it is outdated, `docs/GODOT_ADDENDUM.md` and this plan take precedence. Pure
   rules (combat, lock-on) are ported verbatim in behaviour and covered by the original NUnit tests.
3. **Reuse assets.** Meshes, textures and the prototype art come from the Unity project through Git LFS.
4. **Keep the rules engine-agnostic.** `Ball2.Core` must not reference Godot, so the rules stay
   testable and portable to a future engine.
5. **Ship a clean project.** No `Assets/`, `Packages/`, `ProjectSettings/`, Unity `.meta` files or
   Unity CI on the Godot branch.

---

## 2. Repository layout

| Unity (`main`) | Godot (`godot-rewrite`) | Notes |
|---|---|---|
| `Assets/` | `assets/` | Lowercase: the Unity tree is deleted on this branch, and macOS is case-insensitive |
| `Assets/Docs/` | `docs/` | Design/spec documents kept as reference |
| `Assets/Scripts/Core/` | `Ball2.Core/` | Engine-agnostic rules, `System.Numerics` instead of `UnityEngine` |
| `Assets/Scripts/Gameplay/` | `scripts/Gameplay/` | Godot `Node`/`RigidBody3D` scripts |
| `Assets/Scripts/Gameplay/UIScript.cs` | `scripts/UI/UIScript.cs` | HUD, pause/controls and lose screens |
| `Assets/Scripts/Gameplay/*.cs` (audio) | `scripts/Audio/AudioManager.cs` | Generated sound set, autoload |
| `Assets/Tests/EditMode/` | `tests/Ball2.Core.Tests/` | NUnit, run with `dotnet test` |
| `Assets/Prefabs/*.prefab` | `scenes/*.tscn` | Scenes are authored for Godot, not converted YAML |
| `ProjectSettings/`, `Packages/` | `project.godot`, `*.csproj` | Godot + .NET configuration |
| `.github/workflows/ci.yml` (Unity) | `.github/workflows/godot.yml` | LFS checkout, import, build, tests, smoke |
| — | `export_presets.cfg` | Linux, Windows and macOS export presets |

The active course is `Assets/Scenes/Test2.unity` (the 500 × 500 m, 41-box course), transcribed into
`scenes/arena_test2.tscn` and instanced by `scenes/main.tscn`. Each box keeps its original position,
size and rotation, converted from Unity's left-handed space to Godot's right-handed space. The player,
enemy and camera spawn points come from the same scene (`PlayerBall` at the origin, one `EnemyBall`
at `(-25.2, 2.3, 27.8)` in Unity coordinates). Spawn heights are lowered to the Godot ball's resting
height (`1.05` m centre) so the initial drop stays below the wall-damage threshold.

The course is wrapped in a `NavigationRegion3D` with `scenes/arena_test2_navmesh.tres`, baked from the
41 static box colliders with a 1.1 m agent radius. `EnemyAI` paths through `NavigationAgent3D` and
falls back to direct steering only when no navigation map is available.

`Assets/Scenes/Test.unity`, the earlier 100 × 100 m prototype, is ported as an optional comparison
scene: `scenes/arena_test.tscn` + `scenes/main_test.tscn` (two enemies, own baked navmesh). Test2
remains the active course.

Tuning taken from the Unity project: gravity `-40` (`DynamicsManager.asset`), player
`gravityScale = 0.8`, rigid-body angular damping `20`, camera FOV `75` with `0.1..5000` clip planes,
camera `distance = 10`, `sensitivity = 0.1`, `minPitch = -30`, `maxPitch = 60` and a warm directional
light of intensity `1.3`.

---

## 3. Architecture

```
Ball2.Core (no Godot)
├── Combat/CombatResolver      pure momentum-differential damage + impulse + perfect hit
└── Combat/LockOnResolver      pure acquisition, soft tracking, break and commit rules
        ▲                                   ▲
        │ ContactInput / ContactOutcome     │ LockOnInput / LockOnResult
        │                                   │
scripts/Gameplay/CombatAdapter.cs     scripts/Gameplay/LockOnAdapter.cs
        ▲                                   ▲
BallMovement / EnemyAI (RigidBody3D)   BallMovement (boost commit flag)
```

- **Core** uses `System.Numerics.Vector3` and has no scene-tree, time or RNG dependencies.
- **Adapters** gather Godot state each physics tick, call the resolver, and apply the results.
- **Gameplay scripts** own movement, camera, grappling, health visuals, enemy behaviour and UI.

### Unity → Godot API mapping

| Unity | Godot |
|---|---|
| `MonoBehaviour.Update` | `Node._Process` |
| `MonoBehaviour.FixedUpdate` | `Node._PhysicsProcess` |
| `Rigidbody` + `SphereCollider` | `RigidBody3D` + `CollisionShape3D` (`SphereShape3D`) |
| `PhysicMaterial` | `PhysicsMaterial` |
| `OnCollisionStay` / `ContactPoint` | `contact_monitor` + `PhysicsDirectBodyState3D` contacts in `_IntegrateForces` |
| `Input.GetAxis` / `Input Manager` | input actions in `project.godot` + `Input.GetVector` |
| `Camera` orbit + `Physics.Raycast` | `Camera3D` + `PhysicsRayQueryParameters3D` |
| `NavMeshAgent` (enemy corners) | `NavigationRegion3D` + baked `NavigationMesh` + `NavigationAgent3D` |
| `AudioSource` / imported clips | `AudioStreamPlayer` / `AudioStreamPlayer3D` + generated WAV set |
| `ParticleSystem` | `CPUParticles3D` |
| `Canvas` / `TextMeshProUGUI` | `CanvasLayer` / `Control` / `Label` / `ProgressBar` |
| `Prefab` | `PackedScene` / `.tscn` |
| `ScriptableObject`-style config | plain C# config types in `Ball2.Core` |
| `UnityTest` / NUnit | NUnit + `dotnet test` |
| `SpriteFaceCamera` | `Sprite3D` billboard mode |
| `SlowRotate` | not needed; ball spin comes from rigid-body physics |

Deliberate divergences:

- **Test2 is the active course.** The active Unity scene is `Test2.unity` (500 × 500 m, 41 colliders,
  tall towers, one enemy). The older 100 × 100 `Test.unity` prototype is available as an optional
  comparison scene (`main_test.tscn`), not the default.
- **No ceiling.** `Test2.unity` has no ceiling; its four walls are 20 m high and the course is open
  above. This is kept as authored, so a fast boost can carry the ball over a wall.
- **Translucent balls.** The Unity ball texture is opaque, which hid the hamster sprite. The Godot
  materials use alpha `0.68` plus a subtle rim light so the hamster reads from outside.
- **Original hamster art.** The Unity `HamsterSprite` GUID resolves to no file in the repository;
  `assets/sprites/hamster.svg` is an original Godot replacement drawn for this branch.
- **Baked navigation.** The Unity NavMesh bake is not convertible. The Godot navmesh is baked from the
  static box colliders (`arena_test2_navmesh.tres`, 357 polygons, 1.1 m agent radius) and used by
  `NavigationAgent3D`; direct steering remains as a fallback for isolated test scenes.
- **Spawn heights.** Unity spawns the balls at `y = 2.3`/`1.4`; Godot's 1 m-radius balls spawn at the
  resting centre height (`1.05`) so the initial drop does not register as wall damage.
- **Generated scenes.** Unity scene YAML is not converted automatically; the arena and prefabs were
  rebuilt as native `.tscn` files from the parsed layout and component values.
- **Generated audio.** The Unity project had no audio sources, so the port ships a small synthesized
  WAV set (boost, dodge, hit, grapple, death, UI) rather than copied clips.
- **Legacy UI removed.** The strawberry counter and the stubbed win screen were remnants of an
  earlier iteration of the project, not part of the current game. They are intentionally not ported;
  the lose screen, enemy counter, pause/controls menu and HUD remain.

---

## 4. Parity status

| System | Status | Notes |
|---|---|---|
| Arena / course | ✅ Ported | `Test2.unity`: 500 × 500 m floor, 41 boxes, one enemy, no ceiling |
| Optional legacy course | ✅ Ported | `Test.unity` → `arena_test.tscn` / `main_test.tscn`, two enemies |
| Ball movement (roll, air strafe, speed caps) | ✅ Ported | Ground probe fixed; gravity 40, angular damping 20 |
| Boost charge/launch + lock-on commit | ✅ Ported | Charge lift, release impulse, UI bar |
| Dodge left/right + cooldown | ✅ Ported | Cooldown bar and particle burst |
| Third-person orbit camera | ✅ Ported | Collision pull-in, lazy/reactive smoothing |
| Dual grapple hooks | ✅ Ported | Spring/damper rope, reel, auto-aim, cursor states |
| Momentum combat | ✅ Ported | `CombatResolver` + 14 tests; contact adapter |
| Lock-on | ✅ Ported | `LockOnResolver` + 22 tests; billboard icon |
| Health + five damage textures | ✅ Ported | Player death → lose screen; enemy death → despawn |
| Enemy AI (detect, chase, wander, boost) | ✅ Ported | Baked navmesh + `NavigationAgent3D`; direct fallback |
| HUD / pause / controls / lose / main menu | ✅ Ported | Enemy counter, boost/dodge bars, debug text, rebinding |
| Strawberry counter / win screen | ⚫ Removed | Earlier-iteration remnants; intentionally not ported |
| Audio | ✅ Ported | Generated WAV set + pooled 2D/3D players |
| Input remapping UI | ✅ Ported | Pause → Controls; saved to `user://input_bindings.cfg` |
| Export presets / CI | ✅ Ported | Linux/Windows/macOS presets + GitHub Actions build/test job |

---

## 5. Test strategy

1. **Rules tests** — the original 37 NUnit tests run against `Ball2.Core`:
   14 combat and 22 lock-on plus a canary. They cover thresholds, symmetric/asymmetric impulses,
   wall contacts, perfect-hit windows, acquisition, soft tracking, break conditions, commit-on-charge,
   determinism and degenerate input.
2. **Gameplay integration tests** — `tests/GameplayTests.tscn` loads the real `main.tscn` and runs
   50 checks: spawn safety and enemy counter, arena/navmesh routing around a tower, damage textures,
   lock-on acquire/break, grapple fire/hook/return, ball-to-ball contact damage, pause/resume,
   controls panel, audio samples, input rebinding, enemy death/despawn and player death → lose screen.
   The runner exits non-zero on failure.
3. **Headless smoke tests** — load the menu, main and legacy scenes with `--headless --quit-after N`
   and check for script/runtime errors.
4. **Visual smoke test** — run the main scene with the movie writer
   (`--write-movie`) and inspect frames for camera orientation, arena, hamster visibility, HUD and
   lock icon.
5. **CI** — `.github/workflows/godot.yml` checks out LFS assets, imports the project with Godot .NET,
   builds, runs both test suites and smoke-tests the main scene.

Commands:

```bash
dotnet test tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj
godot --headless --path . res://tests/GameplayTests.tscn
godot --headless --path . res://scenes/main.tscn --quit-after 180
```

---

## 6. Remaining work (ordered)

All six items from the original migration list are complete:

1. **Navigation for enemies** — ✅ `NavigationRegion3D` + pre-baked Test2 navmesh, `EnemyAI` uses
   `NavigationAgent3D` while preserving Unity chase/wander ranges.
2. **Gameplay integration tests** — ✅ `tests/GameplayTests.tscn`, 50 checks covering grapple, contact
   damage, lock-on, damage textures, pause, controls, audio/rebinding and death/despawn.
3. **Performance pass** — ✅ Test2 holds 120 Hz/8.33 ms on the M1 Pro; auto-aim stays throttled; shadow
   range reduced from 400 m to 250 m; the earlier hitch was movie-writer contention/first-run shaders.
4. **Polish** — ✅ original hamster SVG, tuned transparency/rim lighting and procedural sky, triplanar
   arena scale, generated audio, pause-menu input remapping, export presets and Godot CI.
5. **Optional legacy arena** — ✅ `Test.unity` ported as `arena_test.tscn` / `main_test.tscn` with its
   own navmesh and two enemies; Test2 remains active.
6. **Docs** — ✅ README, this plan, `docs/GODOT_ADDENDUM.md` and the branch `AGENTS.md` are current.
   The Unity `BALL2_SPEC.md` remains historical reference.

Future work (not required for parity):

- **Netcode** — the Unity spec's Fishnet server-authoritative design is not implemented; `Ball2.Core`
  remains deterministic and ready for it.
- **Export templates / signed builds** — presets exist, but each machine/CI job must install the
  Godot 4.7.2 .NET export templates before exporting.
- **Feel tuning** — damage/knockback, camera and audio levels remain exposed for tuning; particle
  effects could be upgraded beyond `CPUParticles3D` placeholders.
- **More content** — additional arenas, enemies or pickups if the design moves beyond the two Unity
  scenes.

---

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Core rules drift from Unity behaviour | Original NUnit tests ported unchanged in intent; resolvers kept pure |
| Physics feel differs (Godot vs PhysX) | Tune mass, bounce, friction, gravity scale and impulses against the Unity values; visual smoke tests |
| Contact ordering causes double resolution | Lower-instance-id guard resolves each ball pair once |
| Arena geometry mis-parsed | Layout comes from the Unity scene; a visual pass checks walls/obstacles |
| macOS case-insensitive collision with `Assets/` | Unity tree removed on this branch; assets live in lowercase `assets/` |
| Git LFS assets missing on clone | `.gitattributes` keeps PNG/WAV in LFS; `scripts/setup.sh` runs `git lfs install/pull` and fails on pointer files; CI checks out with `lfs: true` and verifies asset headers |
| .NET Godot vs standard Godot confusion | README states the .NET build requirement explicitly |

# Ball2 — Unity → Godot Migration Plan

**Source:** `Tom1tk/ball2`, branch `main`, Unity 6000.4.10f1 (Built-in Render Pipeline).
**Target:** `Tom1tk/ball2`, branch `godot-rewrite`, Godot 4.7.x .NET with C# and .NET 8.
**Approach:** a parallel branch, like `bevy-rewrite`, rather than an in-place conversion. The Godot
project sits at the branch root; the Unity project stays untouched on `main`.

---

## 1. Goals and constraints

1. **Keep C#.** Gameplay stays in C#; no GDScript in the shipping game.
2. **Preserve behaviour.** The Unity specs in `docs/` remain the design source of truth. Pure rules
   (combat, lock-on) are ported verbatim in behaviour and covered by the original NUnit tests.
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
| `Assets/Scripts/Gameplay/UIScript.cs` | `scripts/UI/UIScript.cs` | HUD, pause and lose screens |
| `Assets/Tests/EditMode/` | `tests/Ball2.Core.Tests/` | NUnit, run with `dotnet test` |
| `Assets/Prefabs/*.prefab` | `scenes/*.tscn` | Scenes are authored for Godot, not converted YAML |
| `ProjectSettings/`, `Packages/` | `project.godot`, `*.csproj` | Godot + .NET configuration |
| `.github/workflows/ci.yml` (Unity) | — | CI needs a Godot/.NET replacement |

The arena layout (floor, four walls, 14 obstacles) was parsed from the Unity scene and regenerated as
`scenes/main.tscn`; obstacles keep their original position, size and rotation.

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
| `NavMeshAgent` (enemy corners) | direct steering; `NavigationAgent3D` is the planned replacement |
| `ParticleSystem` | `CPUParticles3D` |
| `Canvas` / `TextMeshProUGUI` | `CanvasLayer` / `Control` / `Label` / `ProgressBar` |
| `Prefab` | `PackedScene` / `.tscn` |
| `ScriptableObject`-style config | plain C# config types in `Ball2.Core` |
| `UnityTest` / NUnit | NUnit + `dotnet test` |
| `SpriteFaceCamera` | `Sprite3D` billboard mode |
| `SlowRotate` | not needed; ball spin comes from rigid-body physics |

Deliberate divergences:

- **Closed arena.** A ceiling was added at y ≈ 30 so the arena is a closed box; walls were raised to
  match. This avoids losing the ball over the walls and keeps the momentum game bounded.
- **Translucent balls.** The Unity ball texture is opaque, which hid the hamster sprite. The Godot
  materials are translucent (alpha ≈ 0.55) so the hamster reads from outside.
- **Placeholder hamster.** The Unity `HamsterSprite` GUID resolves to no file in the repository;
  `assets/sprites/hamster.svg` is an explicit stand-in.
- **Direct steering AI.** The Unity enemy used a NavMesh for path corners. The prototype arena is
  open, so the port steers directly and falls back to wandering. Restoring `NavigationAgent3D` is on
  the remaining-work list.
- **Generated scenes.** Unity scene YAML is not converted automatically; the arena and prefabs were
  rebuilt as native `.tscn` files from the parsed layout and component values.

---

## 4. Parity status

| System | Status | Notes |
|---|---|---|
| Ball movement (roll, air strafe, speed caps) | ✅ Ported | Ground probe fixed to start above the floor |
| Boost charge/launch + lock-on commit | ✅ Ported | Charge lift, release impulse, UI bar |
| Dodge left/right + cooldown | ✅ Ported | Cooldown bar and particle burst |
| Third-person orbit camera | ✅ Ported | Collision pull-in, lazy/reactive smoothing |
| Dual grapple hooks | ✅ Ported | Spring/damper rope, reel, auto-aim, cursor states |
| Momentum combat | ✅ Ported | `CombatResolver` + 14 tests; contact adapter |
| Lock-on | ✅ Ported | `LockOnResolver` + 22 tests; billboard icon |
| Health + five damage textures | ✅ Ported | Player death → lose screen; enemy death → despawn |
| Enemy AI (detect, chase, wander, boost) | 🟡 Ported without NavMesh | Direct steering; pathfinding remains |
| HUD / pause / lose / main menu | ✅ Ported | Enemy counter, boost/dodge bars, debug text |
| Strawberry counter | 🟡 UI only | No pickup entities in the Unity source either |
| Win screen | ⚪ Not ported | Unity code contains a stubbed `showWinUI` |
| Audio | ⚪ Not ported | No audio sources found in the Unity gameplay scripts |
| Input remapping UI | ⚪ Not ported | Input map lives in `project.godot` |
| Export presets / CI | ⚪ Not ported | Needs Godot export templates and a CI job |

---

## 5. Test strategy

1. **Rules tests** — the original 37 NUnit tests run against `Ball2.Core`:
   14 combat and 22 lock-on plus a canary. They cover thresholds, symmetric/asymmetric impulses,
   wall contacts, perfect-hit windows, acquisition, soft tracking, break conditions, commit-on-charge,
   determinism and degenerate input.
2. **Headless smoke tests** — load the menu and main scenes with `--headless --quit-after N` and
   check for script/runtime errors.
3. **Visual smoke test** — run the main scene with the movie writer
   (`--write-movie`) and inspect frames for camera orientation, arena, hamster visibility, HUD and
   lock icon.
4. **Not yet covered** — automated integration tests for grappling, damage application and AI; these
   should be added as Godot scene tests once the remaining gameplay work lands.

Commands:

```bash
dotnet test tests/Ball2.Core.Tests/Ball2.Core.Tests.csproj
godot --headless --path . res://scenes/main.tscn --quit-after 180
```

---

## 6. Remaining work (ordered)

1. **Navigation for enemies** — add a `NavigationRegion3D` over the arena and move `EnemyAI` to a
   `NavigationAgent3D`, preserving the Unity chase/wander ranges.
2. **Pickups and win flow** — add strawberry entities and a win screen; re-enable the counter.
3. **Integration tests** — grappling hook state machine, contact damage, enemy death/despawn.
4. **Performance pass** — auto-aim raycasts are throttled to ~8 Hz; profile on the target Mac and
   consider lowering shadow range/glow if GPU-bound.
5. **Polish** — replace the placeholder hamster, tune ball translucency/lighting, add audio, wire an
   input-remap screen, add export presets and a Godot CI job.
6. **Docs** — fold the Godot decisions back into `docs/BALL2_SPEC.md` (or add a Godot addendum) so the
   design source of truth matches the branch.

---

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Core rules drift from Unity behaviour | Original NUnit tests ported unchanged in intent; resolvers kept pure |
| Physics feel differs (Godot vs PhysX) | Tune mass, bounce, friction, gravity scale and impulses against the Unity values; visual smoke tests |
| Contact ordering causes double resolution | Lower-instance-id guard resolves each ball pair once |
| Arena geometry mis-parsed | Layout comes from the Unity scene; a visual pass checks walls/obstacles |
| macOS case-insensitive collision with `Assets/` | Unity tree removed on this branch; assets live in lowercase `assets/` |
| Git LFS assets missing on clone | `.gitattributes` keeps PNG/audio in LFS; clone with `git lfs install` |
| .NET Godot vs standard Godot confusion | README states the .NET build requirement explicitly |

# Ball2 — Bevy rewrite

Physics hamster-ball aerial combat, ported from the Unity project to **Bevy 0.20**
with **Avian 0.8** physics and **bevy_hanabi** GPU particles.

The playable Unity scene is `Test2.unity`; the game starts directly in the arena
(`Menu.unity` is disabled and `Main.unity` is missing in the reference project).

## Run

```bash
cargo run --release
```

The first build takes a while. On Linux you need the usual Bevy system packages
(X11/Wayland, Vulkan, ALSA, udev). On CachyOS/Arch:

```bash
sudo pacman -S --needed base-devel pkgconf alsa-lib libx11 libxkbcommon wayland vulkan-icd-loader libxcb libxrandr libxi libxcursor libxinerama
```

### In-game development UI

A live hierarchy/component inspector is available behind the optional `editor`
feature:

```bash
cargo run --release --features editor
```

Press `F1` to show or hide it. The panel exposes the live entity hierarchy, all
reflected components and resources (including the gameplay tuning resources),
and allows values to be edited while the game runs. The cursor is released while
it is open and recaptured when it closes. This is an inspector, not a full
Unity-style scene editor; it is intended for debugging and tuning the running
ECS world.

## Controls

| Input | Action |
|---|---|
| `WASD` | Steer (camera-relative) |
| Mouse | Camera / aim |
| `Space` (hold, release) | Charge and release boost |
| `Q` / `E` | Dodge left / right |
| Left mouse | Fire left grapple hook |
| Right mouse | Fire right grapple hook |
| `Shift` (hold) | Reel in |
| `Esc` | Pause / resume |
| Click resume / restart / quit | Pause-menu actions |
| `R` / `Q` while paused | Restart / quit (port convenience; the Unity pause menu uses buttons) |
| `F3` | Toggle the Unity `Debug` text and aim gizmos |
| `F1` | Toggle the development inspector (`editor` feature) |

## Port status

- [x] `CombatResolver` momentum-differential damage/knockback (pure Rust, 18 tests)
- [x] `LockOnResolver` acquisition, soft tracking, break and commit-on-charge (pure Rust, 19 tests)
- [x] Player controller: reduced gravity, camera-relative steering, boost charge,
      dodge, speed caps and core-colour speed response
- [x] Dual grapple hooks: projectiles, view-facing rope ribbons, rope constraint,
      reel, release boost and swing anti-gravity
- [x] Health/damage states, Unity's shell damage textures, broken shell, enemy
      and player death behaviour
- [x] Lock-on boost assist and the world-space green lock indicator
- [x] Enemy AI: wander / chase / boost-attack state machine and charge effect
- [x] Third-person camera with Unity yaw/pitch, SmoothDamp smoothing and
      sphere-cast collision
- [x] Exact `Test2.unity` arena: 41 box colliders, spawn points, camera FOV and
      clip planes, directional light and ambient/clear colours
- [x] Real authored assets: prototype arena textures, ball shell/damage textures,
      crosshair/cursor sprites and lock icon
- [x] HUD and pause menu: crosshair, hook cursors, boost/dodge sliders, Controls
      and Debug text, 1920x1080 pause layout and resume/restart/quit buttons
- [x] Hanabi particle ports of `ChargeFX`, `SpeedFX` and `PhaseFX` (the particle
      system Unity actually assigns to `BallMovement.DodgeFX`)
- [x] Restart cleanup and respawn, including the deceptively named `PhaseFX` child

All Unity prefab tuning values live in `src/gameplay/config.rs` as Bevy
resources, and the pure resolver configs live in `src/core/`. Ball-ball contact
damage uses the pre-solver contact momentum. Static arena collisions are left to
the physics material and deal no combat damage, matching the Unity project where
only opponent hits damaged the balls.

### Known deviations / reference gaps

- The Unity enemy navigates with a NavMesh. The port keeps the same
  detection/chase/wander/boost state machine and tuning but steers directly
  toward its target; it has no obstacle-avoidance pathfinding.
- `Test2.unity` contains no strawberry instances even though the Unity scripts
  contain the trigger handler. The port keeps a pickup/health hook but spawns
  none, matching the scene.
- The exact standalone hamster sprite cannot be recovered because its GUID is
  absent from the reference project. The visible authored `BallTex` shell
  texture is ported and used.
- UI is rebuilt with Bevy UI at the authored 1920x1080 coordinates; text font
  metrics differ slightly from TextMeshPro.

The Unity reference project is kept outside this repository.

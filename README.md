# Ball2 — Bevy rewrite

Physics hamster-ball aerial combat, ported from the Unity project to **Bevy 0.20** with **Avian 0.8** physics.

## Run

```bash
cargo run --release
```

The first build takes a while. On Linux you need the usual Bevy system packages (X11/Wayland, Vulkan, ALSA, udev). On CachyOS/Arch:

```bash
sudo pacman -S --needed base-devel pkgconf alsa-lib libx11 libxkbcommon wayland vulkan-icd-loader libxcb libxrandr libxi libxcursor libxinerama
```

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
| `R` | Restart (while paused or defeated) |
| `Q` (on pause/game-over) | Quit |
| `F3` | Debug overlay / aim gizmos |

## Port status

- [x] `CombatResolver` momentum-differential damage/knockback (pure Rust, 18 tests)
- [x] `LockOnResolver` acquisition, soft tracking, break and commit-on-charge (pure Rust, 19 tests)
- [x] Player controller (reduced gravity, strafe, boost, dodge, speed caps)
- [x] Dual grapple hooks (projectiles, rope constraint, reel, release boost, swing anti-gravity)
- [x] Health/damage states, material damage colours, enemy/player death
- [x] Lock-on boost assist
- [x] Enemy AI (wander / chase / boost attack)
- [x] Third-person camera with sphere-cast collision
- [x] Arena, strawberry pickups, HUD, pause/game-over overlay and restart

The game starts directly in the arena. All Unity prefab tuning values live in
`src/gameplay/config.rs` as Bevy resources and in the pure resolver configs, so
feel can be adjusted without touching the systems. Wall impacts use the
pre-physics contact momentum, which makes high-speed wall/ceiling hits
dangerous; lower `CombatSettings` or the ball speed caps to soften that.

The AI keeps Unity's detection/chase/wander/boost state machine, but steers
directly toward its target instead of using a NavMesh. The original Unity
project is preserved for reference outside this repository.

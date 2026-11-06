//! Unity effect ports: `ChargeFX`, `SpeedFX`, and the mesh-clone afterimages.
//!
//! The Unity prefabs are simulated particle systems, not sprites:
//!
//! - `ChargeFX`: 50/s, 0.5 s life, -5 speed toward the centre, 2 m sphere,
//!   1 m particles stretched 3x, white/yellow fade.
//! - `SpeedFX`: 150/s, 0.5 s life, 30 m/s outward on a 3 m ring, 0.1 m wide
//!   particles stretched into 5 m radial speed lines.
//! - `PhaseFX`: 40/s for 0.25 s, authored 2 x 1 m mesh-sphere particles over a
//!   0.2 s life. PlayerBall.prefab assigns this prefab to
//!   `BallMovement.DodgeFX`; the separate (unused) `DodgeFX.prefab` is not
//!   instantiated. The port renders full-size translucent ball clones with a
//!   slightly longer 0.35 s trail so the dodge afterimage stays readable.
//!
//! The boost also leaves a trail of full-size ball clones. Unity's boost trail
//! is the camera-mounted `SpeedFX` ring; the clone trail reproduces the
//! afterimage read from the authored PhaseFX mesh particles.

use bevy::light::NotShadowCaster;
use bevy::prelude::*;
use bevy_hanabi::{
    AlphaMode as ParticleAlphaMode, Attribute, ColorOverLifetimeModifier, EffectAsset,
    EffectSpawner, ExprWriter, Gradient, Module, OrientMode, OrientModifier, ParticleEffect,
    RoundModifier, SetAttributeModifier, SetPositionCircleModifier, SetPositionSphereModifier,
    SetVelocityCircleModifier, SetVelocitySphereModifier, ShapeDimension, SimulationSpace,
    SpawnerSettings,
};

use super::components::*;

/// Unity PhaseFX emission rate and duration.
const DODGE_GHOST_RATE: f32 = 40.0;
const DODGE_GHOST_DURATION: f32 = 0.25;
// Unity PhaseFX fades each clone over 0.2 s. The port's clones are the only
// dodge visual, and 0.2 s at dodge speed produces a very short trail, so keep
// the authored 40/s emission but let each clone linger for 0.35 s.
const DODGE_GHOST_LIFE: f32 = 0.35;
// `DodgeTex.mat` is 0.1176 alpha, but the texture it references is missing
// from the reference project. The ball shell is used instead, and the
// authored alpha is too faint to read against the arena; lift it so the
// clones stay visible across the short dodge window.
const DODGE_GHOST_ALPHA: f32 = 0.35;

/// Port-added ball-clone trail during boost.
const BOOST_GHOST_RATE: f32 = 40.0;
const BOOST_GHOST_LIFE: f32 = 0.3;
const BOOST_GHOST_ALPHA: f32 = 0.22;

#[derive(Resource)]
pub struct FxAssets {
    pub charge: Handle<EffectAsset>,
    pub speed: Handle<EffectAsset>,
    /// Unit sphere used by the translucent dodge/boost clones.
    pub ghost_mesh: Handle<Mesh>,
}

#[derive(Resource, Default)]
pub struct FxState {
    pub speed_entity: Option<Entity>,
    pub last_speed_timer: f32,
    pub last_dodge_timer: f32,
    pub last_boost_ghost_timer: f32,
    pub dodge_emit_remaining: f32,
    pub boost_emit_remaining: f32,
    pub dodge_ghost_accumulator: f32,
    pub boost_ghost_accumulator: f32,
}

/// A fading mesh clone of the player ball.
#[derive(Component)]
pub struct GhostBall {
    pub age: f32,
    pub lifetime: f32,
    pub base_alpha: f32,
    pub kind: GhostKind,
    pub material: Handle<StandardMaterial>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GhostKind {
    Dodge,
    Boost,
}

pub fn setup_fx_assets(
    mut commands: Commands,
    mut effects: ResMut<Assets<EffectAsset>>,
    mut meshes: ResMut<Assets<Mesh>>,
) {
    let charge = effects.add(charge_effect());
    let speed = effects.add(speed_effect());
    commands.insert_resource(FxAssets {
        charge,
        speed,
        ghost_mesh: meshes.add(Sphere::new(1.0).mesh().ico(4).unwrap()),
    });
}

fn alpha_fade(r: f32, g: f32, b: f32, alpha: f32) -> Gradient<Vec4> {
    let mut gradient = Gradient::new();
    gradient.add_key(0.0, Vec4::new(r, g, b, alpha));
    gradient.add_key(0.5, Vec4::new(r, g, b, alpha));
    gradient.add_key(1.0, Vec4::new(r, g, b, 0.0));
    gradient
}

fn charge_effect() -> EffectAsset {
    let writer = ExprWriter::new();
    let init_age = SetAttributeModifier::new(Attribute::AGE, writer.lit(0.0).expr());
    let init_life = SetAttributeModifier::new(Attribute::LIFETIME, writer.lit(0.5).expr());
    let init_pos = SetPositionSphereModifier {
        center: writer.lit(Vec3::ZERO).expr(),
        // ChargeFX shape radius 2, world scale 1 (PlayerBall root 2 x prefab
        // local scale 0.5).
        radius: writer.lit(2.0).expr(),
        dimension: ShapeDimension::Surface,
    };
    let init_vel = SetVelocitySphereModifier {
        center: writer.lit(Vec3::ZERO).expr(),
        speed: writer.lit(-5.0).expr(),
    };
    // Stretch renderer with a 3x length scale: 1 m wide, 3 m along the
    // inward velocity.
    let init_size =
        SetAttributeModifier::new(Attribute::SIZE2, writer.lit(Vec2::new(3.0, 1.0)).expr());
    let mut module: Module = writer.finish();
    let round = RoundModifier::constant(&mut module, 1.0);

    EffectAsset::new(
        4096,
        SpawnerSettings::rate(50.0.into()).with_starts_active(false),
        module,
    )
    .with_name("ChargeFX")
    .with_alpha_mode(ParticleAlphaMode::Blend)
    .with_simulation_space(SimulationSpace::Global)
    .init(init_age)
    .init(init_life)
    .init(init_pos)
    .init(init_vel)
    .init(init_size)
    .render(ColorOverLifetimeModifier::new(alpha_fade(
        1.0, 0.857, 0.382, 0.482,
    )))
    .render(OrientModifier::new(OrientMode::AlongVelocity))
    .render(round)
}

fn speed_effect() -> EffectAsset {
    let writer = ExprWriter::new();
    let init_age = SetAttributeModifier::new(Attribute::AGE, writer.lit(0.0).expr());
    let init_life = SetAttributeModifier::new(Attribute::LIFETIME, writer.lit(0.5).expr());
    let init_pos = SetPositionCircleModifier {
        center: writer.lit(Vec3::ZERO).expr(),
        axis: writer.lit(Vec3::Z).expr(),
        radius: writer.lit(3.0).expr(),
        dimension: ShapeDimension::Surface,
    };
    let init_vel = SetVelocityCircleModifier {
        center: writer.lit(Vec3::ZERO).expr(),
        axis: writer.lit(Vec3::Z).expr(),
        speed: writer.lit(30.0).expr(),
    };
    // `OrientMode::AlongVelocity` points the particle's local X axis along its
    // velocity. The long axis must therefore be X: a 5 m streak across the
    // direction of travel and only 0.1 m wide, i.e. radial speed lines.
    let init_size =
        SetAttributeModifier::new(Attribute::SIZE2, writer.lit(Vec2::new(5.0, 0.1)).expr());
    let mut module: Module = writer.finish();
    let round = RoundModifier::constant(&mut module, 1.0);

    EffectAsset::new(8192, SpawnerSettings::rate(150.0.into()), module)
        .with_name("SpeedFX")
        .with_alpha_mode(ParticleAlphaMode::Blend)
        .with_simulation_space(SimulationSpace::Global)
        .init(init_age)
        .init(init_life)
        .init(init_pos)
        .init(init_vel)
        .init(init_size)
        .render(ColorOverLifetimeModifier::new(alpha_fade(
            1.0, 1.0, 1.0, 0.482,
        )))
        .render(OrientModifier::new(OrientMode::AlongVelocity))
        .render(round)
}

/// Enable or clear each ball's charge effect when the Unity `ChargeFX.Play` /
/// `Stop(..., StopEmittingAndClear)` calls would happen. Despawning the effect
/// entity is the Hanabi equivalent of clearing existing particles.
pub fn update_charge_fx(
    mut commands: Commands,
    fx: Res<FxAssets>,
    mut balls: Query<(Entity, &mut BallVisual, &BallState, &Health)>,
    mut spawners: Query<&mut EffectSpawner>,
) {
    for (entity, mut visual, state, health) in &mut balls {
        let active = state.charging && !health.dead;

        if active && visual.charge_fx.is_none() {
            let effect = commands
                .spawn((
                    ChildOf(entity),
                    ParticleEffect::new(fx.charge.clone()),
                    Transform::IDENTITY,
                ))
                .id();
            visual.charge_fx = Some(effect);
        } else if !active && visual.charge_fx.is_some() {
            if let Some(effect) = visual.charge_fx.take() {
                commands.entity(effect).despawn();
            }
        }

        if let Some(effect) = visual.charge_fx {
            if let Ok(mut spawner) = spawners.get_mut(effect) {
                spawner.active = active;
            }
        }
    }
}

/// Spawn the dodge and boost afterimages on the fixed clock. Dodge uses the
/// authored PhaseFX 40/s, 0.25 s cadence; boost adds a denser clone trail.
pub fn emit_ghost_clones(
    time: Res<Time<Fixed>>,
    mut commands: Commands,
    mut materials: ResMut<Assets<StandardMaterial>>,
    fx: Res<FxAssets>,
    stats: Res<GameStats>,
    mut state: ResMut<FxState>,
    player_query: Query<(&Transform, &Health, &BallVisual), With<Player>>,
) {
    let Ok((player_transform, health, visual)) = player_query.single() else {
        return;
    };
    if health.dead {
        return;
    }
    let dt = time.delta_secs();
    if dt <= 0.0 {
        return;
    }

    // Rising edge of the dodge timer starts the 0.25 s PhaseFX emission.
    let dodge_started = stats.dodge_fx_timer > state.last_dodge_timer + 1.0e-4;
    state.last_dodge_timer = stats.dodge_fx_timer;
    if dodge_started {
        state.dodge_emit_remaining = DODGE_GHOST_DURATION;
        state.dodge_ghost_accumulator = 0.0;
        spawn_ghost(
            &mut commands,
            &mut materials,
            &fx.ghost_mesh,
            visual,
            health,
            player_transform,
            GhostKind::Dodge,
        );
    }

    if state.dodge_emit_remaining > 0.0 {
        let active_dt = state.dodge_emit_remaining.min(dt);
        state.dodge_ghost_accumulator += active_dt * DODGE_GHOST_RATE;
        let count = state.dodge_ghost_accumulator.floor().min(2.0) as usize;
        state.dodge_ghost_accumulator -= count as f32;
        for _ in 0..count {
            spawn_ghost(
                &mut commands,
                &mut materials,
                &fx.ghost_mesh,
                visual,
                health,
                player_transform,
                GhostKind::Dodge,
            );
        }
        state.dodge_emit_remaining = (state.dodge_emit_remaining - dt).max(0.0);
    }

    // Rising edge of the boost timer starts the boost clone trail.
    let boost_started = stats.speed_fx_timer > state.last_boost_ghost_timer + 1.0e-4;
    state.last_boost_ghost_timer = stats.speed_fx_timer;
    if boost_started {
        state.boost_emit_remaining = stats.speed_fx_timer;
        state.boost_ghost_accumulator = 0.0;
        spawn_ghost(
            &mut commands,
            &mut materials,
            &fx.ghost_mesh,
            visual,
            health,
            player_transform,
            GhostKind::Boost,
        );
    }

    if state.boost_emit_remaining > 0.0 {
        let active_dt = state.boost_emit_remaining.min(dt);
        state.boost_ghost_accumulator += active_dt * BOOST_GHOST_RATE;
        let count = state.boost_ghost_accumulator.floor().min(2.0) as usize;
        state.boost_ghost_accumulator -= count as f32;
        for _ in 0..count {
            spawn_ghost(
                &mut commands,
                &mut materials,
                &fx.ghost_mesh,
                visual,
                health,
                player_transform,
                GhostKind::Boost,
            );
        }
        state.boost_emit_remaining = (state.boost_emit_remaining - dt).max(0.0);
    }
}

#[allow(clippy::too_many_arguments)]
fn spawn_ghost(
    commands: &mut Commands,
    materials: &mut Assets<StandardMaterial>,
    mesh: &Handle<Mesh>,
    visual: &BallVisual,
    health: &Health,
    player: &Transform,
    kind: GhostKind,
) {
    let (lifetime, base_alpha, scale) = match kind {
        // The authored PhaseFX mesh is 2 x 1 x 1 m, but the flattened
        // silhouette is hard to read as a clone of the ball during the dodge.
        // Use the full ball volume so the dodge trail matches the player and
        // the boost afterimages.
        GhostKind::Dodge => (DODGE_GHOST_LIFE, DODGE_GHOST_ALPHA, Vec3::ONE),
        GhostKind::Boost => (BOOST_GHOST_LIFE, BOOST_GHOST_ALPHA, Vec3::ONE),
    };

    let mut material = materials
        .get(&visual.shell_material)
        .cloned()
        .unwrap_or_default();
    material.base_color_texture = Some(visual.shell_textures[health.damage_tier.min(4)].clone());
    material.base_color = Color::srgba(1.0, 1.0, 1.0, base_alpha);
    material.alpha_mode = AlphaMode::Blend;
    material.perceptual_roughness = 0.7;
    material.metallic = 0.0;
    material.cull_mode = None;

    let handle = materials.add(material);
    commands.spawn((
        GameWorld,
        GhostBall {
            age: 0.0,
            lifetime,
            base_alpha,
            kind,
            material: handle.clone(),
        },
        Mesh3d(mesh.clone()),
        MeshMaterial3d(handle),
        Transform::from_translation(player.translation)
            .with_rotation(player.rotation)
            .with_scale(scale),
        NotShadowCaster,
    ));
}

pub fn update_ghost_balls(
    time: Res<Time>,
    mut commands: Commands,
    mut materials: ResMut<Assets<StandardMaterial>>,
    mut ghosts: Query<(Entity, &mut GhostBall, &MeshMaterial3d<StandardMaterial>)>,
) {
    let dt = time.delta_secs();
    for (entity, mut ghost, material_handle) in &mut ghosts {
        ghost.age += dt;
        let t = (ghost.age / ghost.lifetime).clamp(0.0, 1.0);
        let alpha = ghost.base_alpha * ghost.alpha_curve(ghost.kind, t);
        if let Some(mut material) = materials.get_mut(&material_handle.0) {
            material.base_color = material.base_color.with_alpha(alpha);
        }
        if ghost.age >= ghost.lifetime {
            materials.remove(&ghost.material);
            commands.entity(entity).despawn();
        }
    }
}

impl GhostBall {
    fn alpha_curve(&self, kind: GhostKind, t: f32) -> f32 {
        match kind {
            // PhaseFX alpha keys: 1 at t=0, 0.032 at t=0.4, 0 at t=1.
            GhostKind::Dodge => {
                if t < 0.4 {
                    1.0 + (0.032 - 1.0) * (t / 0.4)
                } else {
                    0.032 * (1.0 - (t - 0.4) / 0.6)
                }
            }
            // SpeedFX alpha keys: full for the first half, then fade out.
            GhostKind::Boost => {
                if t < 0.5 {
                    1.0
                } else {
                    1.0 - (t - 0.5) / 0.5
                }
            }
        }
    }
}

/// Keep the camera-mounted SpeedFX alive for the boost duration. A new boost
/// clears the previous effect (Unity `Stop(..., StopEmittingAndClear)`).
///
/// The emitter is a world-space entity reparented each frame to the camera's
/// transform instead of a camera child. Unity uses a world-space particle
/// simulation on a transform that follows the camera; spawning a plain child
/// here left the effect out of the render hierarchy.
pub fn update_speed_fx(
    mut commands: Commands,
    fx: Res<FxAssets>,
    stats: Res<GameStats>,
    mut state: ResMut<FxState>,
    camera: Query<Entity, With<Camera>>,
    mut spawners: Query<&mut EffectSpawner, With<ParticleEffect>>,
) {
    let timer = stats.speed_fx_timer;
    let started = timer > state.last_speed_timer + 1.0e-4;
    state.last_speed_timer = timer;

    if started {
        if let Some(old) = state.speed_entity.take() {
            commands.entity(old).despawn();
        }
        if let Ok(camera_entity) = camera.single() {
            let effect = commands
                .spawn((
                    ChildOf(camera_entity),
                    ParticleEffect::new(fx.speed.clone()),
                    // Unity SpeedFX local position: (0, -0.7886243, 10.277552)
                    // in a +Z-forward camera. Bevy cameras look down -Z. The
                    // prefab's -99.6X instance rotation plus the shape's -75X
                    // tilt leaves a 15-degree tilt toward the camera basis.
                    Transform::from_xyz(0.0, -0.788_624_3, -10.277_552)
                        .with_rotation(Quat::from_rotation_x(-15.0_f32.to_radians())),
                ))
                .id();
            state.speed_entity = Some(effect);
        }
    }

    if let Some(effect) = state.speed_entity {
        if let Ok(mut spawner) = spawners.get_mut(effect) {
            // The effect starts emitting as soon as it is spawned
            // (`starts_active(true)`); stop the stream when the authored
            // boost duration ends, while existing streaks fade out.
            spawner.active = timer > 0.0;
        }
    }
}

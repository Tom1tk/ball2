//! Unity particle-effect ports (`ChargeFX`, `SpeedFX`, `DodgeFX`, `PhaseFX`).
//!
//! The Unity prefabs are simulated particle systems, not sprites. This module
//! rebuilds them with Hanabi using the serialized values from
//! `Assets/Effects/*.prefab`:
//!
//! - `ChargeFX`: 50/s, 0.5 s life, -5 speed toward the centre, 2 m sphere,
//!   1 m particles stretched 3x, white/yellow fade.
//! - `SpeedFX`: 150/s, 0.5 s life, 30 m/s outward on a 3 m ring, 0.1 m
//!   particles stretched 50x, world-space emitter in front of the camera.
//! - `PhaseFX`: 40/s for 0.25 s, 0.2 s life, 2 x 1 m particles at the emitter,
//!   local to the ball. PlayerBall.prefab assigns this prefab to
//!   `BallMovement.DodgeFX`; the separate (unused) `DodgeFX.prefab` is not
//!   instantiated.

use bevy::prelude::*;
use bevy_hanabi::{
    AlphaMode as ParticleAlphaMode, Attribute, ColorOverLifetimeModifier, EffectAsset,
    EffectSpawner, ExprWriter, Gradient, Module, OrientMode, OrientModifier, ParticleEffect,
    RoundModifier, SetAttributeModifier, SetPositionCircleModifier, SetPositionSphereModifier,
    SetVelocityCircleModifier, SetVelocitySphereModifier, ShapeDimension, SimulationSpace,
    SpawnerSettings,
};

use super::components::*;

#[derive(Resource)]
pub struct FxAssets {
    pub charge: Handle<EffectAsset>,
    pub speed: Handle<EffectAsset>,
    /// `PhaseFX` is the particle system assigned to `BallMovement.DodgeFX` in
    /// PlayerBall.prefab; the separate `DodgeFX.prefab` is unused there.
    pub phase: Handle<EffectAsset>,
}

#[derive(Resource, Default)]
pub struct FxState {
    pub speed_entity: Option<Entity>,
    pub last_dodge_timer: f32,
    pub last_speed_timer: f32,
}

pub fn setup_fx_assets(mut commands: Commands, mut effects: ResMut<Assets<EffectAsset>>) {
    let charge = effects.add(charge_effect());
    let speed = effects.add(speed_effect());
    let phase = effects.add(phase_effect());
    commands.insert_resource(FxAssets {
        charge,
        speed,
        phase,
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
        // Shape radius 2 at the prefab's Local scaling-mode scale 0.5.
        radius: writer.lit(1.0).expr(),
        dimension: ShapeDimension::Surface,
    };
    let init_vel = SetVelocitySphereModifier {
        center: writer.lit(Vec3::ZERO).expr(),
        speed: writer.lit(-2.5).expr(),
    };
    let init_size =
        SetAttributeModifier::new(Attribute::SIZE2, writer.lit(Vec2::new(0.5, 1.5)).expr());
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
    let init_size =
        SetAttributeModifier::new(Attribute::SIZE2, writer.lit(Vec2::new(0.1, 5.0)).expr());
    let mut module: Module = writer.finish();
    let round = RoundModifier::constant(&mut module, 1.0);

    EffectAsset::new(
        8192,
        SpawnerSettings::rate(150.0.into()).with_starts_active(false),
        module,
    )
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

fn phase_effect() -> EffectAsset {
    let writer = ExprWriter::new();
    let init_age = SetAttributeModifier::new(Attribute::AGE, writer.lit(0.0).expr());
    let init_life = SetAttributeModifier::new(Attribute::LIFETIME, writer.lit(0.2).expr());
    let init_vel = SetAttributeModifier::new(Attribute::VELOCITY, writer.lit(Vec3::ZERO).expr());
    let init_pos = SetPositionSphereModifier {
        center: writer.lit(Vec3::ZERO).expr(),
        radius: writer.lit(0.0001).expr(),
        dimension: ShapeDimension::Surface,
    };
    let init_size =
        SetAttributeModifier::new(Attribute::SIZE2, writer.lit(Vec2::new(2.0, 1.0)).expr());
    let mut module: Module = writer.finish();
    let round = RoundModifier::constant(&mut module, 1.0);

    let mut gradient = Gradient::new();
    gradient.add_key(0.0, Vec4::new(1.0, 1.0, 1.0, 0.118));
    gradient.add_key(0.4, Vec4::new(1.0, 1.0, 1.0, 0.0038));
    gradient.add_key(1.0, Vec4::splat(0.0));

    // Unity: 0.25 s duration, non-looping, 40/s, starts inactive.
    EffectAsset::new(
        1024,
        SpawnerSettings::rate(40.0.into())
            .with_spawn_duration(0.25.into())
            .with_period(0.25.into())
            .with_cycle_count(1)
            .with_starts_active(false),
        module,
    )
    .with_name("PhaseFX")
    .with_alpha_mode(ParticleAlphaMode::Blend)
    .with_simulation_space(SimulationSpace::Local)
    .init(init_age)
    .init(init_life)
    .init(init_vel)
    .init(init_pos)
    .init(init_size)
    .render(ColorOverLifetimeModifier::new(gradient))
    .render(OrientModifier::new(OrientMode::FaceCameraPosition))
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

/// Fire the player's DodgeFX once per dodge. `GameStats::dodge_fx_timer` is
/// raised by the movement system; a rising edge is a dodge.
pub fn update_dodge_fx(
    stats: Res<GameStats>,
    mut state: ResMut<FxState>,
    mut spawners: Query<&mut EffectSpawner, With<PhaseFx>>,
) {
    if stats.dodge_fx_timer > state.last_dodge_timer + 1.0e-4 {
        if let Ok(mut spawner) = spawners.single_mut() {
            spawner.reset();
            spawner.active = true;
        }
    }
    state.last_dodge_timer = stats.dodge_fx_timer;
}

/// Keep the camera-mounted SpeedFX alive for the boost duration. A new boost
/// clears the previous effect (Unity `Stop(..., StopEmittingAndClear)`).
pub fn update_speed_fx(
    mut commands: Commands,
    fx: Res<FxAssets>,
    stats: Res<GameStats>,
    mut state: ResMut<FxState>,
    camera: Query<Entity, With<Camera>>,
    mut spawners: Query<&mut EffectSpawner>,
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
                    // in a +Z-forward camera. Bevy cameras look down -Z.
                    // The prefab's -90X transform plus the shape's -75X tilt
                    // becomes a 15-degree tilt toward the camera basis.
                    Transform::from_xyz(0.0, -0.788_624_3, -10.277_552)
                        .with_rotation(Quat::from_rotation_x(-15.0_f32.to_radians())),
                ))
                .id();
            state.speed_entity = Some(effect);
        }
    }

    if let Some(effect) = state.speed_entity {
        if let Ok(mut spawner) = spawners.get_mut(effect) {
            spawner.active = timer > 0.0;
        }
    }
}

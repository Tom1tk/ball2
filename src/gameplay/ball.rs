//! Player ball controller, health visuals and death handling.
//!
//! Ported from `BallMovement.cs`, `HealthScript.cs` and the material-switch
//! half of the Unity prefabs. Forces/inputs live here; physics itself is Avian.

use avian3d::prelude::*;
use bevy::prelude::*;

use super::components::*;
use super::config::BallConfig;
use super::grapple::GrappleState;
use super::input::PlayerInput;
use super::lockon::PlayerLockOn;
use super::state::GameState;

pub fn player_movement(
    time: Res<Time<Fixed>>,
    input: Res<PlayerInput>,
    cfg: Res<BallConfig>,
    grapple: Res<GrappleState>,
    mut lockon: ResMut<PlayerLockOn>,
    mut stats: ResMut<GameStats>,
    mut player_query: Query<
        (
            Entity,
            &Transform,
            Forces,
            &mut BallMovement,
            &mut BallState,
            &Health,
        ),
        With<Player>,
    >,
    camera_query: Query<&GlobalTransform, With<Camera>>,
    spatial: SpatialQuery,
) {
    let Ok((entity, transform, mut forces, mut movement, mut state, health)) =
        player_query.single_mut()
    else {
        return;
    };

    if health.dead {
        state.speed = forces.linear_velocity().length();
        movement.speed = state.speed;
        return;
    }

    let dt = time.delta_secs();
    let position = transform.translation;
    let velocity = forces.linear_velocity();
    let speed = velocity.length();
    let grounded = is_grounded(&spatial, entity, position);

    movement.speed = speed;
    movement.grounded = grounded;
    state.speed = speed;
    state.grounded = grounded;

    let camera_transform = camera_query.iter().next();
    let camera_forward = camera_transform
        .map(|t| t.forward().as_vec3())
        .unwrap_or(Vec3::NEG_Z);
    let camera_right = camera_transform
        .map(|t| t.right().as_vec3())
        .unwrap_or(Vec3::X);

    let forward = Vec3::new(camera_forward.x, 0.0, camera_forward.z).normalize_or_zero();
    let right = Vec3::new(camera_right.x, 0.0, camera_right.z).normalize_or_zero();

    let direction_input = right * input.movement.x + forward * input.movement.y;
    let relative_direction = direction_input.normalize_or_zero();

    let hooked = grapple.any_hooked();

    // Unity: while below ground cap and not charging, or while hooked, add input force.
    if (speed < cfg.max_velocity && !movement.charging) || hooked {
        let factor = if hooked { cfg.air_strafe_force } else { 1.0 };
        forces.apply_force(relative_direction * cfg.input_force * factor);
    }

    if input.boost_pressed && !movement.charging {
        *forces.linear_velocity_mut() *= 0.3;
        movement.boost_lv = 0.1;
        movement.charging = true;
        lockon.boost_charge_started = true;
    }

    if movement.charging {
        if movement.boost_lv < cfg.boost_max {
            movement.boost_lv = (movement.boost_lv + dt).min(cfg.boost_max);
        }
        // Unity adds upward acceleration while charging: gravity.magnitude * 0.85.
        forces.apply_linear_acceleration(Vec3::Y * cfg.gravity * cfg.charge_lift_scale);
    }

    if input.boost_released && movement.charging {
        let duration = (movement.boost_lv * 3.0) / 10.0;
        stats.speed_fx_timer = duration;

        let mut boost_dir = if lockon.current.has_lock() {
            (lockon.current.tracking_position - position).normalize_or_zero()
        } else {
            camera_forward.normalize_or_zero()
        };
        if boost_dir == Vec3::ZERO {
            boost_dir = camera_forward.normalize_or_zero();
        }

        forces.apply_linear_impulse(boost_dir * (movement.boost_lv * cfg.boost_force));
        movement.boost_lv = 0.0;
        movement.charging = false;
        lockon.boost_charge_started = false;
    }

    // Dodge cooldown/timer and side impulse.
    if movement.dodge_timer < cfg.dodge_cooldown {
        movement.dodge_timer = (movement.dodge_timer + dt).min(cfg.dodge_cooldown);
    }

    if (input.dodge_left || input.dodge_right) && movement.dodge_timer >= cfg.dodge_cooldown {
        let sign = if input.dodge_left { -1.0 } else { 1.0 };
        let dodge_dir = Vec3::new(right.x, 0.0, right.z) * sign;
        forces.apply_linear_impulse(dodge_dir * cfg.dodge_force);
        movement.dodge_timer = 0.0;
        stats.dodge_fx_timer = 0.25;
    }

    // Unity clamps to maxVelocity when grounded, airMaxVelocity otherwise.
    let cap = if grounded {
        cfg.max_velocity
    } else {
        cfg.air_max_velocity
    };
    let current_velocity = forces.linear_velocity();
    if current_velocity.length() > cap {
        *forces.linear_velocity_mut() = current_velocity.normalize_or_zero() * cap;
    }

    // Keep the damage model's "momentum at contact" independent of the solver.
    state.speed = forces.linear_velocity().length();
    state.charging = movement.charging;
}

/// Unity's grounded ray: origin 0.55 below centre, length 0.65.
pub fn is_grounded(spatial: &SpatialQuery, entity: Entity, position: Vec3) -> bool {
    let origin = position + Vec3::NEG_Y * 0.55;
    let filter = SpatialQueryFilter::default().with_excluded_entities([entity]);
    spatial
        .cast_ray(origin, Dir3::NEG_Y, 0.65, true, &filter)
        .is_some()
}

pub fn record_pre_physics_velocity(
    mut query: Query<(&LinearVelocity, &mut PrePhysicsVelocity), With<Ball>>,
) {
    for (velocity, mut previous) in &mut query {
        previous.0 = velocity.0;
    }
}

pub fn update_ball_visuals(
    time: Res<Time>,
    cfg: Res<BallConfig>,
    mut materials: ResMut<Assets<StandardMaterial>>,
    mut visuals: Query<(&mut Health, &BallState, &BallVisual)>,
    mut fx_query: Query<(&mut Transform, &mut Visibility), With<ChargeFx>>,
) {
    let dt = time.delta_secs();
    let mut t = 0.0;

    for (mut health, state, visual) in &mut visuals {
        health.flash = (health.flash - dt).max(0.0);
        t += dt;

        let base = health.damage_color();
        let srgba = base.to_srgba();
        let flash = health.flash;

        if let Some(mut material) = materials.get_mut(&visual.shell_material) {
            material.base_color = Color::srgba(srgba.red, srgba.green, srgba.blue, 0.39);
            material.emissive = LinearRgba::rgb(flash * 2.5, flash * 1.5, flash * 1.0);
        }

        let core_color = if health.dead {
            Color::srgb(0.04, 0.035, 0.05)
        } else if state.charging {
            Color::srgb(1.0, 0.18, 0.85)
        } else if state.speed > cfg.moving_speed_threshold {
            Color::srgb(0.1, 0.95, 1.0)
        } else {
            Color::srgb(0.75, 0.95, 1.0)
        };
        let core_linear = core_color.to_linear();
        if let Some(mut material) = materials.get_mut(&visual.core_material) {
            material.base_color = core_color;
            material.emissive = LinearRgba::rgb(
                core_linear.red * 1.8,
                core_linear.green * 1.8,
                core_linear.blue * 1.8,
            );
        }

        if let Ok((mut fx_transform, mut visibility)) = fx_query.get_mut(visual.charge_fx) {
            if state.charging && !health.dead {
                *visibility = Visibility::Visible;
                let pulse = 1.0 + (t * 9.0).sin() * 0.08;
                fx_transform.scale = Vec3::splat(pulse);
            } else {
                *visibility = Visibility::Hidden;
            }
        }

        if let Some(mut material) = materials.get_mut(&visual.charge_material) {
            material.emissive = LinearRgba::rgb(2.5, 0.3, 1.8);
        }
    }
}

pub fn process_deaths(
    mut commands: Commands,
    mut stats: ResMut<GameStats>,
    mut next_state: ResMut<NextState<GameState>>,
    state: Res<State<GameState>>,
    mut query: Query<(Entity, &mut Health, Option<&Player>, Option<&Enemy>), With<Ball>>,
) {
    for (entity, mut health, is_player, is_enemy) in &mut query {
        if !health.dead || health.death_processed {
            continue;
        }

        health.death_processed = true;

        if is_player.is_some() {
            stats.outcome = Some(Outcome::Defeat);
            if *state.get() == GameState::Playing {
                next_state.set(GameState::GameOver);
            }
        }

        if is_enemy.is_some() {
            stats.enemies_alive = stats.enemies_alive.saturating_sub(1);
            commands.entity(entity).remove::<Enemy>();
            commands.entity(entity).remove::<super::enemy::EnemyAi>();
            commands.entity(entity).insert(DespawnAfter {
                timer: Timer::from_seconds(1.5, TimerMode::Once),
            });

            if stats.enemies_alive == 0 {
                stats.outcome = Some(Outcome::Victory);
                if *state.get() == GameState::Playing {
                    next_state.set(GameState::GameOver);
                }
            }
        }
    }
}

pub fn tick_despawn(
    time: Res<Time>,
    mut commands: Commands,
    mut query: Query<(Entity, &mut DespawnAfter)>,
) {
    for (entity, mut despawn) in &mut query {
        despawn.timer.tick(time.delta());
        if despawn.timer.just_finished() {
            commands.entity(entity).despawn();
        }
    }
}

pub fn tick_ball_fx(
    time: Res<Time>,
    mut stats: ResMut<GameStats>,
    mut trail: ResMut<SpeedTrail>,
    query: Query<&Transform, With<Player>>,
) {
    let dt = time.delta_secs();
    stats.perfect_hit_flash = (stats.perfect_hit_flash - dt).max(0.0);
    stats.dodge_fx_timer = (stats.dodge_fx_timer - dt).max(0.0);

    if stats.speed_fx_timer > 0.0 {
        stats.speed_fx_timer = (stats.speed_fx_timer - dt).max(0.0);
        trail.active = 0.35;
    }

    if trail.active > 0.0 {
        trail.active = (trail.active - dt).max(0.0);
        if let Some(transform) = query.iter().next() {
            trail.points.push(transform.translation);
            if trail.points.len() > 40 {
                trail.points.remove(0);
            }
        }
    } else if !trail.points.is_empty() {
        trail.points.clear();
    }
}

pub fn spin_pickups(time: Res<Time>, mut query: Query<(&mut Transform, &Spin)>) {
    for (mut transform, spin) in &mut query {
        transform.rotate_y(spin.radians_per_second * time.delta_secs());
    }
}

/// Simplified dodge/perfect-hit feedback in place of the Unity particles.
pub fn draw_player_fx(
    stats: Res<GameStats>,
    player_query: Query<&Transform, With<Player>>,
    mut gizmos: Gizmos,
) {
    let Ok(player) = player_query.single() else {
        return;
    };
    let position = player.translation;

    if stats.dodge_fx_timer > 0.0 {
        let progress = 1.0 - (stats.dodge_fx_timer / 0.25).clamp(0.0, 1.0);
        let radius = 1.0 + progress * 1.8;
        gizmos.circle(
            Isometry3d::from_translation(position),
            radius,
            Color::srgba(1.0, 0.7, 0.15, 1.0 - progress),
        );
    }

    if stats.perfect_hit_flash > 0.0 {
        gizmos.cross(
            Isometry3d::from_translation(position),
            1.4,
            Color::srgba(1.0, 0.95, 0.4, stats.perfect_hit_flash / 0.35),
        );
    }
}

/// Simplified port of Unity's SpeedFX particle trail: draw the recorded path
/// while the boost-release trail timer is alive.
pub fn draw_speed_trail(trail: Res<SpeedTrail>, mut gizmos: Gizmos) {
    if trail.points.len() < 2 || trail.active <= 0.0 {
        return;
    }
    let alpha = (trail.active / 0.35).clamp(0.0, 1.0);
    let color = Color::srgba(0.25, 0.85, 1.0, alpha * 0.7);
    for pair in trail.points.windows(2) {
        gizmos.line(pair[0], pair[1], color);
    }
}

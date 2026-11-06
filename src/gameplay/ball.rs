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

    // Unity BallMovement compensates the non-1 gravityScale with an upward
    // acceleration: Physics.gravity.magnitude * (1 - gravityScale).
    forces.apply_linear_acceleration(Vec3::Y * cfg.gravity * (1.0 - cfg.gravity_scale));

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
    mut visuals: Query<(&mut Health, &BallState, &BallVisual, Option<&Player>)>,
) {
    let dt = time.delta_secs();

    for (mut health, state, visual, is_player) in &mut visuals {
        health.flash = (health.flash - dt).max(0.0);

        // Unity HealthScript.materialSwitch picks the shell material by the
        // absolute health value; DMGbroken is fully transparent.
        let tier = health.damage_tier.min(4);
        if let Some(mut material) = materials.get_mut(&visual.shell_material) {
            material.base_color_texture = Some(visual.shell_textures[tier].clone());
            let alpha = if tier == 4 { 0.0 } else { 0.392 };
            material.base_color = Color::srgba(1.0, 1.0, 1.0, alpha);
        }

        // Unity BallMovement.changeSphereColour: charging = SimpleRed,
        // moving faster than 1 m/s = SimpleGreen, otherwise SimpleBlue. This
        // only exists on the player; EnemyBall has no BallMovement and keeps
        // its SimpleBlue core.
        if is_player.is_some() {
            let core_color = if state.charging {
                Color::srgb(1.0, 0.0, 0.059_367_18)
            } else if state.speed > cfg.moving_speed_threshold {
                Color::srgb(0.0, 1.0, 0.138_429_64)
            } else {
                Color::srgb(0.0, 0.604_660_03, 1.0)
            };
            if let Some(mut material) = materials.get_mut(&visual.core_material) {
                material.base_color = core_color;
            }
        }
    }
}

/// Unity's `HealthScript.died` flag has no death/game-over reaction: the ball
/// keeps its broken (transparent) material and enemies simply despawn. This
/// system only performs the enemy cleanup that exists in the scene scripts.
pub fn process_deaths(
    mut commands: Commands,
    mut stats: ResMut<GameStats>,
    mut query: Query<(Entity, &mut Health, Option<&Player>, Option<&Enemy>), With<Ball>>,
) {
    for (entity, mut health, is_player, is_enemy) in &mut query {
        if !health.dead || health.death_processed {
            continue;
        }

        health.death_processed = true;

        if is_player.is_some() {
            // Unity SetActive(false): HealthScript disables the PlayerBall
            // GameObject, which hides the whole hierarchy and disables both
            // the body and its collider. Visibility is explicitly Hidden on
            // the root; the core child inherits it.
            commands.entity(entity).insert((
                RigidBodyDisabled,
                ColliderDisabled,
                Visibility::Hidden,
                LinearVelocity::ZERO,
                AngularVelocity::ZERO,
            ));
        }

        if is_enemy.is_some() {
            stats.enemies_alive = stats.enemies_alive.saturating_sub(1);
            commands.entity(entity).remove::<Enemy>();
            commands.entity(entity).remove::<super::enemy::EnemyAi>();
            commands.entity(entity).insert(DespawnAfter {
                timer: Timer::from_seconds(1.5, TimerMode::Once),
            });
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

pub fn tick_ball_fx(time: Res<Time>, mut stats: ResMut<GameStats>) {
    let dt = time.delta_secs();
    stats.dodge_fx_timer = (stats.dodge_fx_timer - dt).max(0.0);
    stats.speed_fx_timer = (stats.speed_fx_timer - dt).max(0.0);
}

pub fn spin_pickups(time: Res<Time>, mut query: Query<(&mut Transform, &Spin)>) {
    for (mut transform, spin) in &mut query {
        transform.rotate_y(spin.radians_per_second * time.delta_secs());
    }
}

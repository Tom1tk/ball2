//! Third-person camera ported from `CameraFollow.cs`.
//!
//! Unity's camera looks down +Z; Bevy cameras look down -Z, so the orbit is
//! expressed with the camera placed behind at +Z. The smoothing, sphere-cast
//! collision, lazy/reactive speed blend and pitch clamps match the source.

use avian3d::prelude::*;
use bevy::input::mouse::AccumulatedMouseMotion;
use bevy::prelude::*;

use super::components::{CameraFollow, Player};
use super::config::CameraConfig;
use super::state::GameState;

pub fn camera_follow(
    time: Res<Time>,
    state: Res<State<GameState>>,
    mouse: Res<AccumulatedMouseMotion>,
    cfg: Res<CameraConfig>,
    mut camera_query: Query<(&mut Transform, &mut CameraFollow), (With<Camera>, Without<Player>)>,
    player_query: Query<
        (Entity, &Transform, Option<&LinearVelocity>),
        (With<Player>, Without<Camera>),
    >,
    spatial: SpatialQuery,
) {
    if *state.get() != GameState::Playing {
        return;
    }

    let Ok((mut camera_tf, mut follow)) = camera_query.single_mut() else {
        return;
    };
    let Ok((player_entity, player_tf, player_velocity)) = player_query.single() else {
        return;
    };

    let dt = time.delta_secs();
    if dt <= 0.0 {
        return;
    }

    // Unity yaw/pitch are in degrees; positive pitch orbits the camera upward.
    let sensitivity = cfg.sensitivity.to_radians();
    follow.yaw += mouse.delta.x * sensitivity;
    follow.pitch -= mouse.delta.y * sensitivity;
    follow.pitch = follow
        .pitch
        .clamp(cfg.min_pitch.to_radians(), cfg.max_pitch.to_radians());

    let rotation = Quat::from_rotation_y(follow.yaw) * Quat::from_rotation_x(-follow.pitch);
    let dir_to_camera = rotation * Vec3::Z;

    let look_target = player_tf.translation + Vec3::Y * cfg.vertical_offset;

    let shape = Collider::sphere(cfg.collision_radius);
    let filter = SpatialQueryFilter::default().with_excluded_entities([player_entity]);
    let cast_config = ShapeCastConfig::from_max_distance(cfg.distance);
    let hit = spatial.cast_shape(
        &shape,
        look_target,
        Quat::IDENTITY,
        Dir3::new(dir_to_camera).unwrap_or(Dir3::Z),
        &cast_config,
        &filter,
    );
    let target_distance = hit
        .map(|hit| hit.distance.clamp(cfg.min_distance, cfg.distance))
        .unwrap_or(cfg.distance);

    follow.current_distance = smooth_damp_f32(
        follow.current_distance,
        target_distance,
        &mut follow.distance_velocity,
        cfg.collision_smooth_time,
        dt,
    );

    let desired_position = look_target + dir_to_camera * follow.current_distance;

    let mut speed = cfg.reactive_speed;
    if let Some(velocity) = player_velocity {
        if velocity.0.length_squared() > 0.01 {
            let cam_to_target = (player_tf.translation - camera_tf.translation).normalize_or_zero();
            let dot = cam_to_target
                .dot(velocity.0.normalize_or_zero())
                .clamp(0.0, 1.0);
            speed = cfg.reactive_speed.lerp(cfg.lazy_speed, dot);
        }
    }

    let smoothed = smooth_damp_vec3(
        camera_tf.translation,
        desired_position,
        &mut follow.position_velocity,
        (1.0 / speed.max(0.001)).max(0.0001),
        dt,
    );

    camera_tf.translation = smoothed;
    camera_tf.look_at(look_target, Vec3::Y);
}

/// Unity `Mathf.SmoothDamp` (critically damped spring).
pub fn smooth_damp_f32(
    current: f32,
    target: f32,
    velocity: &mut f32,
    smooth_time: f32,
    dt: f32,
) -> f32 {
    let smooth_time = smooth_time.max(0.0001);
    let omega = 2.0 / smooth_time;
    let x = omega * dt;
    let exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
    let change = current - target;
    let temp = (*velocity + omega * change) * dt;
    *velocity = (*velocity - omega * temp) * exp;
    target + (change + temp) * exp
}

/// Unity `Vector3.SmoothDamp`.
pub fn smooth_damp_vec3(
    current: Vec3,
    target: Vec3,
    velocity: &mut Vec3,
    smooth_time: f32,
    dt: f32,
) -> Vec3 {
    let smooth_time = smooth_time.max(0.0001);
    let omega = 2.0 / smooth_time;
    let x = omega * dt;
    let exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
    let change = current - target;
    let temp = (*velocity + omega * change) * dt;
    *velocity = (*velocity - omega * temp) * exp;
    target + (change + temp) * exp
}

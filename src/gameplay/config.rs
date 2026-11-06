//! Tunable configuration resources. Defaults mirror the serialized values on the
//! Unity prefabs/scenes; they are intentionally exposed for feel-tuning.

use bevy::prelude::*;

use crate::core::combat::CombatConfig;
use crate::core::lockon::LockOnConfig;

/// Bevy resource wrapper for the engine-independent combat tuning.
#[derive(Resource, Clone, Copy, Debug, Default)]
pub struct CombatSettings(pub CombatConfig);

#[derive(Resource, Clone, Debug)]
pub struct BallConfig {
    /// Physics radius. The Unity prefab root is scaled 2x with a 0.5 collider radius.
    pub radius: f32,
    /// Inner visual sphere radius (Unity child sphere is 0.4 local at 2x root).
    pub core_radius: f32,
    pub max_velocity: f32,
    pub air_max_velocity: f32,
    pub input_force: f32,
    pub dodge_force: f32,
    pub dodge_cooldown: f32,
    pub boost_force: f32,
    pub boost_max: f32,
    pub moving_speed_threshold: f32,
    /// Unity Physics.gravity magnitude.
    pub gravity: f32,
    /// Unity gravityScale on BallMovement.
    pub gravity_scale: f32,
    /// Input force multiplier while any hook is attached.
    pub air_strafe_force: f32,
    /// Extra upward acceleration while charging, as a fraction of gravity.
    pub charge_lift_scale: f32,
}

impl Default for BallConfig {
    fn default() -> Self {
        Self {
            radius: 1.0,
            core_radius: 0.4,
            max_velocity: 20.0,
            air_max_velocity: 50.0,
            input_force: 100.0,
            dodge_force: 25.0,
            dodge_cooldown: 1.0,
            boost_force: 70.0,
            boost_max: 1.0,
            moving_speed_threshold: 1.0,
            gravity: 40.0,
            gravity_scale: 0.8,
            air_strafe_force: 0.5,
            charge_lift_scale: 0.85,
        }
    }
}

#[derive(Resource, Clone, Debug)]
pub struct EnemyConfig {
    pub max_velocity: f32,
    pub input_force: f32,
    pub detection_range: f32,
    pub boost_attack_range: f32,
    pub boost_charge_time: f32,
    pub boost_cooldown: f32,
    pub boost_force: f32,
    pub wander_input_scale: f32,
    pub path_update_interval: f32,
    pub min_wander_time: f32,
    pub max_wander_time: f32,
}

impl Default for EnemyConfig {
    fn default() -> Self {
        Self {
            max_velocity: 15.0,
            input_force: 100.0,
            detection_range: 15.0,
            boost_attack_range: 7.0,
            boost_charge_time: 1.5,
            boost_cooldown: 2.0,
            boost_force: 50.0,
            wander_input_scale: 0.5,
            path_update_interval: 0.2,
            min_wander_time: 1.0,
            max_wander_time: 5.0,
        }
    }
}

#[derive(Resource, Clone, Debug)]
pub struct CameraConfig {
    pub distance: f32,
    /// Degrees per mouse pixel (Unity sensitivity = 0.1).
    pub sensitivity: f32,
    pub vertical_offset: f32,
    pub min_pitch: f32,
    pub max_pitch: f32,
    pub collision_radius: f32,
    pub min_distance: f32,
    pub collision_smooth_time: f32,
    pub lazy_speed: f32,
    pub reactive_speed: f32,
}

impl Default for CameraConfig {
    fn default() -> Self {
        Self {
            distance: 10.0,
            sensitivity: 0.1,
            vertical_offset: 1.5,
            min_pitch: -30.0,
            max_pitch: 60.0,
            collision_radius: 0.3,
            min_distance: 1.5,
            collision_smooth_time: 0.1,
            lazy_speed: 8.0,
            reactive_speed: 25.0,
        }
    }
}

#[derive(Resource, Clone, Debug)]
pub struct GrappleConfig {
    pub max_range: f32,
    pub projectile_speed: f32,
    pub return_speed: f32,
    pub reel_acceleration: f32,
    pub max_reel_speed: f32,
    pub min_rope_length: f32,
    pub rope_stiffness: f32,
    pub rope_damping: f32,
    pub stuck_angle_threshold: f32,
    pub indicator_offset: f32,
    pub swing_anti_gravity: f32,
    pub release_boost_multiplier: f32,
    pub anti_wall_force: f32,
    pub manual_aim_radius: f32,
    /// Unity Physics.gravity magnitude; used by swing anti-gravity.
    pub gravity_magnitude: f32,
}

impl Default for GrappleConfig {
    fn default() -> Self {
        Self {
            max_range: 80.0,
            projectile_speed: 300.0,
            return_speed: 300.0,
            reel_acceleration: 30.0,
            max_reel_speed: 60.0,
            min_rope_length: 1.0,
            rope_stiffness: 800.0,
            rope_damping: 15.0,
            stuck_angle_threshold: 170.0,
            indicator_offset: 1.5,
            swing_anti_gravity: 0.3,
            release_boost_multiplier: 1.2,
            anti_wall_force: 5.0,
            manual_aim_radius: 0.08,
            gravity_magnitude: 40.0,
        }
    }
}

#[derive(Resource, Clone, Debug)]
pub struct LockOnSettings {
    pub config: LockOnConfig,
}

impl Default for LockOnSettings {
    fn default() -> Self {
        Self {
            config: LockOnConfig::default(),
        }
    }
}

#[derive(Resource, Clone, Debug)]
pub struct HealthSettings {
    pub player_max_health: i32,
    pub enemy_max_health: i32,
    pub strawberry_heal: i32,
}

impl Default for HealthSettings {
    fn default() -> Self {
        Self {
            player_max_health: 4,
            enemy_max_health: 3,
            strawberry_heal: 1,
        }
    }
}

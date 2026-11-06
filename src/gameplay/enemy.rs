//! Enemy AI ported from `EnemyAI.cs`.
//!
//! Unity's version follows a NavMesh path. Bevy has no built-in NavMesh, so this
//! port keeps the same detection/chase/wander/boost state machine and applies
//! the same forces toward the current steering target. The state machine and
//! tuning values are unchanged; only the path-corner source is substituted.

use avian3d::prelude::*;
use bevy::prelude::*;
use rand::Rng;

use super::ball::is_grounded;
use super::components::*;
use super::config::EnemyConfig;

#[derive(Component)]
pub struct EnemyAi {
    pub can_see_player: bool,
    pub can_attack_player: bool,
    pub attacked: bool,
    pub boost_started: bool,
    pub charge_timer: f32,
    pub cooldown_timer: f32,
    pub path_timer: f32,
    pub chase_dir: Vec3,
    pub wander_point: Vec3,
    pub wander_timer: f32,
}

impl EnemyAi {
    pub fn new() -> Self {
        Self {
            can_see_player: false,
            can_attack_player: false,
            attacked: false,
            boost_started: false,
            charge_timer: 0.0,
            cooldown_timer: 0.0,
            path_timer: 0.0,
            chase_dir: Vec3::ZERO,
            wander_point: Vec3::ZERO,
            wander_timer: 0.0,
        }
    }
}

impl Default for EnemyAi {
    fn default() -> Self {
        Self::new()
    }
}

#[derive(Resource, Clone, Copy, Debug)]
pub struct ArenaBounds {
    pub half_extents: Vec3,
    pub floor_y: f32,
}

impl Default for ArenaBounds {
    fn default() -> Self {
        Self {
            half_extents: Vec3::new(55.0, 30.0, 55.0),
            floor_y: 0.0,
        }
    }
}

pub fn enemy_ai(
    time: Res<Time<Fixed>>,
    cfg: Res<EnemyConfig>,
    bounds: Res<ArenaBounds>,
    mut enemies: Query<
        (
            Entity,
            &Transform,
            Forces,
            &mut EnemyAi,
            &mut BallState,
            &Health,
        ),
        With<Enemy>,
    >,
    player_query: Query<(&Transform, &Health), (With<Player>, Without<Enemy>)>,
    spatial: SpatialQuery,
) {
    let Ok((player_transform, player_health)) = player_query.single() else {
        return;
    };
    let player_position = player_transform.translation;
    let player_alive = !player_health.dead;
    let dt = time.delta_secs();

    for (entity, transform, mut forces, mut ai, mut state, health) in &mut enemies {
        if health.dead {
            state.speed = forces.linear_velocity().length();
            state.charging = false;
            continue;
        }

        let position = transform.translation;
        let distance = position.distance(player_position);
        let grounded = is_grounded(&spatial, entity, position);

        // Unity uses Physics.CheckSphere, so the player's 1 m collider extends
        // both detection ranges by its radius.
        // Unity's CheckSphere stops detecting the player when HealthScript
        // deactivates the PlayerBall GameObject on death.
        ai.can_see_player = player_alive && distance <= cfg.detection_range + 1.0;
        ai.can_attack_player = player_alive && distance <= cfg.boost_attack_range + 1.0;

        // Boost attack takes priority and locks out normal steering until the cooldown ends.
        if ai.boost_started {
            if ai.charge_timer > 0.0 {
                ai.charge_timer -= dt;

                if ai.charge_timer <= 0.0 && !ai.attacked {
                    ai.attacked = true;
                    let direction = (player_position - position).normalize_or_zero();
                    forces.apply_linear_impulse(direction * cfg.boost_force);
                    ai.cooldown_timer = cfg.boost_cooldown;
                }
            } else {
                ai.cooldown_timer -= dt;
                if ai.cooldown_timer <= 0.0 {
                    ai.attacked = false;
                    ai.boost_started = false;
                }
            }
        } else if ai.can_attack_player && ai.can_see_player {
            ai.boost_started = true;
            ai.attacked = false;
            ai.charge_timer = cfg.boost_charge_time;
            let velocity = forces.linear_velocity_mut();
            velocity.x = 0.0;
            velocity.z = 0.0;
        } else if grounded {
            if ai.can_see_player {
                // Chase: repath every 0.2s like the source, steering toward the player.
                ai.path_timer -= dt;
                if ai.path_timer <= 0.0 {
                    ai.path_timer = cfg.path_update_interval;
                    ai.chase_dir = horizontal_dir(player_position - position);
                }
                forces.apply_force(ai.chase_dir * cfg.input_force);
            } else {
                // Wander: pick a random point in the arena and hold it for 1-5s.
                if ai.wander_timer <= 0.0 {
                    let mut rng = rand::rng();
                    ai.wander_point = Vec3::new(
                        rng.random_range(-bounds.half_extents.x..bounds.half_extents.x) * 0.8,
                        bounds.floor_y + 1.0,
                        rng.random_range(-bounds.half_extents.z..bounds.half_extents.z) * 0.8,
                    );
                    ai.wander_timer = rng.random_range(cfg.min_wander_time..cfg.max_wander_time);
                } else {
                    ai.wander_timer -= dt;
                }
                let direction = horizontal_dir(ai.wander_point - position);
                forces.apply_force(direction * cfg.input_force * cfg.wander_input_scale);
            }
        }

        // Unity never reads `maxVelocity` in EnemyAI; the serialized field is
        // left unused. Do not clamp here or boost speed becomes 15 m/s.
        let velocity = forces.linear_velocity();
        state.speed = velocity.length();
        state.grounded = grounded;
        state.charging = ai.boost_started && ai.charge_timer > 0.0;
    }
}

fn horizontal_dir(v: Vec3) -> Vec3 {
    Vec3::new(v.x, 0.0, v.z).normalize_or_zero()
}

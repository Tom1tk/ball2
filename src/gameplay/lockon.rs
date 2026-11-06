//! Player-side lock-on adapter and boost-assist state (B2-010/B2-011 port).

use avian3d::prelude::*;
use bevy::prelude::*;

use crate::core::lockon::{
    LockOnCandidate, LockOnConfig, LockOnInput, LockOnResolver, LockOnResult, LockState,
};

use super::components::*;
use super::state::GameState;

#[derive(Resource)]
pub struct PlayerLockOn {
    pub config: LockOnConfig,
    pub current: LockState,
    pub result: LockOnResult,
    pub boost_charge_started: bool,
    pub locked_enemy_position: Option<Vec3>,
    pub tracking_position: Option<Vec3>,
}

impl Default for PlayerLockOn {
    fn default() -> Self {
        Self {
            config: LockOnConfig::default(),
            current: LockState::NONE,
            result: LockOnResult::none(),
            boost_charge_started: false,
            locked_enemy_position: None,
            tracking_position: None,
        }
    }
}

pub fn update_lockon(
    state: Res<State<GameState>>,
    mut lockon: ResMut<PlayerLockOn>,
    player_query: Query<&Transform, With<Player>>,
    camera_query: Query<&GlobalTransform, With<Camera>>,
    enemy_query: Query<(Entity, &Transform, &LinearVelocity, &Health), With<Enemy>>,
) {
    if *state.get() != GameState::Playing {
        return;
    }

    let Ok(player_transform) = player_query.single() else {
        return;
    };
    let Ok(camera_transform) = camera_query.single() else {
        return;
    };

    let aim = camera_transform.forward().as_vec3().normalize_or_zero();
    let mut candidates = Vec::new();
    for (entity, transform, velocity, health) in &enemy_query {
        if health.dead {
            continue;
        }
        candidates.push(LockOnCandidate::new(
            entity.index().index() as i32,
            transform.translation,
            velocity.0,
        ));
    }

    let input = LockOnInput::new(
        player_transform.translation,
        aim,
        candidates,
        lockon.current,
        lockon.boost_charge_started,
    );
    let result = LockOnResolver::resolve(&input, &lockon.config);

    lockon.current = result.new_lock;
    lockon.result = result;
    lockon.tracking_position = if result.new_lock.has_lock() {
        Some(result.new_lock.tracking_position)
    } else {
        None
    };

    lockon.locked_enemy_position = if result.new_lock.has_lock() {
        enemy_query
            .iter()
            .find(|(entity, _, _, _)| entity.index().index() as i32 == result.new_lock.target_id)
            .map(|(_, transform, _, _)| transform.translation)
    } else {
        None
    };
}

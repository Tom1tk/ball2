//! Bevy gameplay layer: player, grapple, combat, AI, camera, HUD and arena.

pub mod arena;
pub mod ball;
pub mod camera;
pub mod combat;
pub mod components;
pub mod config;
pub mod enemy;
pub mod grapple;
pub mod input;
pub mod lockon;
pub mod state;
pub mod ui;

use bevy::prelude::*;

use components::*;
use config::*;
use grapple::GrappleState;
use input::PlayerInput;
use lockon::PlayerLockOn;
use state::GameState;

pub struct Ball2Plugin;

impl Plugin for Ball2Plugin {
    fn build(&self, app: &mut App) {
        app.init_state::<GameState>()
            .init_resource::<BallConfig>()
            .init_resource::<EnemyConfig>()
            .init_resource::<CameraConfig>()
            .init_resource::<HealthSettings>()
            .init_resource::<CombatSettings>()
            .init_resource::<PlayerInput>()
            .init_resource::<DebugSettings>()
            .init_resource::<GameStats>()
            .init_resource::<WorldSpawned>()
            .init_resource::<GrappleState>()
            .init_resource::<PlayerLockOn>()
            .init_resource::<SpeedTrail>()
            .init_resource::<enemy::ArenaBounds>()
            .add_systems(Startup, (arena::setup_persistent, ui::setup_ui).chain())
            .add_systems(
                OnEnter(GameState::Playing),
                (
                    state::enter_playing,
                    grapple::setup_grapple_assets,
                    arena::spawn_world,
                )
                    .chain(),
            )
            .add_systems(OnEnter(GameState::Paused), state::enter_paused)
            .add_systems(OnExit(GameState::Paused), state::exit_paused)
            .add_systems(OnEnter(GameState::GameOver), state::enter_game_over)
            .add_systems(OnExit(GameState::GameOver), state::exit_game_over)
            .add_systems(OnEnter(GameState::Restarting), (arena::despawn_world, state::enter_restarting).chain())
            .add_systems(
                Update,
                (
                    state::handle_state_input,
                    input::read_player_input,
                    camera::camera_follow,
                    grapple::update_grapple_aiming,
                    lockon::update_lockon,
                    combat::resolve_ball_collisions,
                    arena::pickup_collisions,
                    ball::update_ball_visuals,
                    ball::process_deaths,
                    ball::tick_despawn,
                    ball::tick_ball_fx,
                    ball::spin_pickups,
                    ui::update_hud,
                    grapple::draw_grapple_gizmos,
                )
                    .chain(),
            )
            .add_systems(
                FixedUpdate,
                (
                    grapple::update_grapple,
                    ball::player_movement,
                    enemy::enemy_ai,
                    ball::record_pre_physics_velocity,
                    input::consume_fixed_input,
                )
                    .chain(),
            );
    }
}

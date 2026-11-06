//! Bevy gameplay layer: player, grapple, combat, AI, camera, HUD and arena.

pub mod arena;
pub mod ball;
pub mod camera;
pub mod combat;
pub mod components;
pub mod config;
pub mod effects;
pub mod enemy;
pub mod grapple;
pub mod input;
pub mod lockon;
pub mod state;
pub mod ui;

use avian3d::prelude::PhysicsSystems;
use bevy::prelude::*;
use bevy_hanabi::HanabiPlugin;

use components::*;
use config::*;
use grapple::GrappleState;
use input::PlayerInput;
use lockon::PlayerLockOn;
use state::GameState;

pub struct Ball2Plugin;

impl Plugin for Ball2Plugin {
    fn build(&self, app: &mut App) {
        app.add_plugins(HanabiPlugin)
            .init_state::<GameState>()
            .register_type::<BallConfig>()
            .register_type::<EnemyConfig>()
            .register_type::<CameraConfig>()
            .register_type::<GrappleConfig>()
            .register_type::<HealthSettings>()
            .register_type::<CombatSettings>()
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
            .init_resource::<effects::FxState>()
            .init_resource::<enemy::ArenaBounds>()
            .add_systems(
                Startup,
                (
                    effects::setup_fx_assets,
                    arena::setup_persistent,
                    ui::setup_ui,
                )
                    .chain(),
            )
            .add_systems(
                OnEnter(GameState::Playing),
                (state::enter_playing, grapple::setup_grapple_assets).chain(),
            )
            .add_systems(OnEnter(GameState::Paused), state::enter_paused)
            .add_systems(OnExit(GameState::Paused), state::exit_paused)
            .add_systems(
                OnEnter(GameState::Restarting),
                (arena::despawn_world, state::enter_restarting).chain(),
            )
            .add_systems(
                Update,
                (
                    arena::spawn_world,
                    state::handle_state_input,
                    input::read_player_input,
                    camera::camera_follow,
                    grapple::update_grapple_aiming,
                    grapple::update_rope_visuals,
                    lockon::update_lockon,
                    arena::pickup_collisions,
                    ball::update_ball_visuals,
                    ball::process_deaths,
                    ball::tick_despawn,
                    ball::tick_ball_fx,
                    effects::update_charge_fx,
                    effects::update_dodge_fx,
                    effects::update_speed_fx,
                    ball::spin_pickups,
                    ui::update_hud,
                    grapple::draw_grapple_gizmos,
                )
                    .chain(),
            )
            .add_systems(
                FixedPostUpdate,
                // Resolve contacts in the same physics step as the collision
                // event. Running this in Update batches several fixed steps
                // and reads a stale pre-physics velocity.
                combat::resolve_ball_collisions.after(PhysicsSystems::Writeback),
            )
            .add_systems(
                FixedUpdate,
                (
                    grapple::update_grapple,
                    ball::player_movement,
                    enemy::enemy_ai,
                    ball::tick_contact_grace,
                    ball::record_pre_physics_velocity,
                    input::consume_fixed_input,
                )
                    .chain(),
            );
    }
}

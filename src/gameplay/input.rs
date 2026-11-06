//! Input translation. Mirrors the Unity Input System action map plus the
//! grapple system's direct mouse/shift bindings.

use bevy::prelude::*;

use super::components::DebugSettings;
use super::state::GameState;

#[derive(Resource, Default)]
pub struct PlayerInput {
    pub movement: Vec2,
    pub boost_pressed: bool,
    pub boost_released: bool,
    pub dodge_left: bool,
    pub dodge_right: bool,
    pub fire_left: bool,
    pub fire_right: bool,
    pub release_left: bool,
    pub release_right: bool,
    pub reel_held: bool,
}

impl PlayerInput {
    /// Clear one-shot actions after a fixed step has consumed them. Held state
    /// (movement/reel) is retained and refreshed by `read_player_input`.
    pub fn clear_transient(&mut self) {
        self.boost_pressed = false;
        self.boost_released = false;
        self.dodge_left = false;
        self.dodge_right = false;
        self.fire_left = false;
        self.fire_right = false;
        self.release_left = false;
        self.release_right = false;
    }

    pub fn clear_all(&mut self) {
        *self = Self::default();
    }
}

pub fn read_player_input(
    keys: Res<ButtonInput<KeyCode>>,
    mouse: Res<ButtonInput<MouseButton>>,
    state: Res<State<GameState>>,
    mut input: ResMut<PlayerInput>,
    mut debug: ResMut<DebugSettings>,
) {
    if keys.just_pressed(KeyCode::F3) {
        debug.show_debug_text = !debug.show_debug_text;
        debug.show_aim_gizmos = debug.show_debug_text;
    }
    if *state.get() != GameState::Playing {
        input.clear_all();
        return;
    }

    let x = (keys.pressed(KeyCode::KeyD) as i32 - keys.pressed(KeyCode::KeyA) as i32) as f32;
    let y = (keys.pressed(KeyCode::KeyW) as i32 - keys.pressed(KeyCode::KeyS) as i32) as f32;
    input.movement = Vec2::new(x, y).normalize_or_zero();

    // Edge events are accumulated until the next fixed step consumes them.
    // `just_pressed` is only true for one render frame, and Update can run
    // multiple times between fixed steps, so plain assignment would drop
    // clicks and boost presses.
    input.boost_pressed |= keys.just_pressed(KeyCode::Space);
    input.boost_released |= keys.just_released(KeyCode::Space);
    input.dodge_left |= keys.just_pressed(KeyCode::KeyQ);
    input.dodge_right |= keys.just_pressed(KeyCode::KeyE);

    input.fire_left |= mouse.just_pressed(MouseButton::Left);
    input.fire_right |= mouse.just_pressed(MouseButton::Right);
    input.release_left |= mouse.just_released(MouseButton::Left);
    input.release_right |= mouse.just_released(MouseButton::Right);

    input.reel_held = keys.pressed(KeyCode::ShiftLeft) || keys.pressed(KeyCode::ShiftRight);
}

pub fn consume_fixed_input(mut input: ResMut<PlayerInput>) {
    input.clear_transient();
}

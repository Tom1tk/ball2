//! Game state machine + pause/cursor handling.

use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions};

#[derive(States, Default, Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum GameState {
    #[default]
    Playing,
    Paused,
    Restarting,
}

pub fn handle_state_input(
    keys: Res<ButtonInput<KeyCode>>,
    state: Res<State<GameState>>,
    mut next: ResMut<NextState<GameState>>,
    mut exit: MessageWriter<AppExit>,
) {
    match *state.get() {
        GameState::Playing => {
            if keys.just_pressed(KeyCode::Escape) {
                next.set(GameState::Paused);
            }
        }
        GameState::Paused => {
            if keys.just_pressed(KeyCode::Escape) {
                next.set(GameState::Playing);
            }
            if keys.just_pressed(KeyCode::KeyR) {
                next.set(GameState::Restarting);
            }
            if keys.just_pressed(KeyCode::KeyQ) {
                exit.write(AppExit::Success);
            }
        }
        GameState::Restarting => {}
    }
}

fn cursor_set(
    cursor: &mut Query<&mut CursorOptions, With<Window>>,
    visible: bool,
    grab: CursorGrabMode,
) {
    for mut options in cursor.iter_mut() {
        options.visible = visible;
        options.grab_mode = grab;
    }
}

pub fn enter_playing(
    mut time: ResMut<Time<Virtual>>,
    mut cursor: Query<&mut CursorOptions, With<Window>>,
) {
    time.unpause();
    cursor_set(&mut cursor, false, CursorGrabMode::Confined);
}

pub fn enter_paused(
    mut time: ResMut<Time<Virtual>>,
    mut cursor: Query<&mut CursorOptions, With<Window>>,
) {
    time.pause();
    // Unity UIScript.showPauseUI uses CursorLockMode.Confined: the cursor is
    // visible but cannot leave the game window while the menu is open.
    cursor_set(&mut cursor, true, CursorGrabMode::Confined);
}

pub fn exit_paused(mut time: ResMut<Time<Virtual>>) {
    time.unpause();
}

pub fn enter_restarting(mut time: ResMut<Time<Virtual>>) {
    time.unpause();
}

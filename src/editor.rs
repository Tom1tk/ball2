//! Optional in-game editor, enabled with `--features editor`.
//!
//! This is a development tool rather than a Unity-style scene editor: it gives
//! a live entity hierarchy, a component/resource inspector and gizmos, so the
//! running ECS world can be inspected and tuned. Toggle it with `F1`.

use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions, PrimaryWindow};
use bevy_inspector_egui::bevy_egui::EguiPlugin;
use bevy_inspector_egui::quick::WorldInspectorPlugin;

/// Whether the editor overlay is currently open.
#[derive(Resource, Default, PartialEq, Eq)]
pub struct EditorOpen(pub bool);

pub struct EditorPlugin;

impl Plugin for EditorPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<EditorOpen>()
            .add_plugins(EguiPlugin::default())
            .add_plugins(
                WorldInspectorPlugin::default()
                    .run_if(|open: Res<EditorOpen>| open.0),
            )
            .add_systems(PreUpdate, toggle_editor)
            .add_systems(Startup, log_editor_hint);
    }
}

fn log_editor_hint() {
    info!("Editor enabled: press F1 to toggle the world inspector");
}

/// Toggle the inspector and free/re-capture the cursor while it is open so the
/// egui panels can be used.
fn toggle_editor(
    keys: Res<ButtonInput<KeyCode>>,
    mut open: ResMut<EditorOpen>,
    mut cursor: Query<&mut CursorOptions, With<PrimaryWindow>>,
) {
    if !keys.just_pressed(KeyCode::F1) {
        return;
    }

    open.0 = !open.0;
    let Ok(mut cursor) = cursor.single_mut() else {
        return;
    };

    if open.0 {
        cursor.grab_mode = CursorGrabMode::None;
        cursor.visible = true;
    } else {
        cursor.grab_mode = CursorGrabMode::Confined;
        cursor.visible = false;
    }
}

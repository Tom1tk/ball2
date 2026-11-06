use avian3d::prelude::*;
use ball2::gameplay::Ball2Plugin;
use bevy::prelude::*;

fn main() {
    let mut app = App::new();
    app.add_plugins(DefaultPlugins.set(WindowPlugin {
        primary_window: Some(Window {
            title: "BALL2 - The Baller".to_string(),
            resolution: (1920, 1080).into(),
            resizable: true,
            ..default()
        }),
        ..default()
    }))
    .add_plugins(PhysicsPlugins::default().set(PhysicsInterpolationPlugin::interpolate_all()))
    .insert_resource(Gravity(Vec3::NEG_Y * 40.0))
    .insert_resource(Time::<Fixed>::from_hz(50.0))
    .add_plugins(Ball2Plugin);

    #[cfg(feature = "editor")]
    app.add_plugins(ball2::editor::EditorPlugin);

    app.run();
}

mod core;
mod gameplay;

use avian3d::prelude::*;
use bevy::prelude::*;
use gameplay::Ball2Plugin;

fn main() {
    App::new()
        .add_plugins(DefaultPlugins.set(WindowPlugin {
            primary_window: Some(Window {
                title: "BALL2 - The Baller".to_string(),
                resolution: (1600, 900).into(),
                resizable: true,
                ..default()
            }),
            ..default()
        }))
        .add_plugins(
            PhysicsPlugins::default().set(PhysicsInterpolationPlugin::interpolate_all()),
        )
        .insert_resource(Gravity(Vec3::NEG_Y * 40.0))
        .insert_resource(Time::<Fixed>::from_hz(50.0))
        .add_plugins(Ball2Plugin)
        .run();
}

//! HUD ported from `Test2.unity` and `UIScript`/`BallMovement`/`GrappleSystem`.
//!
//! - 20 px crosshair fixed at screen centre.
//! - 20 px left/right hook cursors at +/-60 px, scaled by hook state.
//! - Two 160 x 30 bottom sliders (boost at y=100, dodge at y=125).
//! - Unity `Controls` text at the top-right and the `Debug` text at the
//!   top-left (enabled by `displayDebugText` on PlayerBall).
//! - PAUSE menu with the exact title/button offsets and font sizes.
//! - World-space lock icon created by `LockOnAdapter` (2.1 m toward the
//!   camera, 1.2 m sprite, green).
//! - `GrappleSystem.OnGUI` manual-aim ring at the locked screen centre.

use bevy::input::mouse::AccumulatedMouseMotion;
use bevy::prelude::*;
use bevy::text::{Justify, TextLayout};
use bevy::ui_widgets::{Activate, Button as UiButton, observe};

use super::components::*;
use super::grapple::{GrappleState, HookState};
use super::input::PlayerInput;
use super::lockon::PlayerLockOn;
use super::state::GameState;

#[derive(Component)]
pub struct DebugText;

#[derive(Component)]
pub struct LockIcon3d;

#[derive(Component, Clone, Copy, PartialEq, Eq)]
pub enum HudElement {
    BoostBar,
    BoostFill,
    BoostHandle,
    DodgeBar,
    DodgeFill,
    DodgeHandle,
    MouseRing,
    Crosshair,
    PauseMenu,
    HookCursorLeft,
    HookCursorRight,
}

pub fn setup_ui(
    mut commands: Commands,
    asset_server: Res<AssetServer>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
) {
    let crosshair = asset_server.load("crosshair.png");
    let left_crosshair = asset_server.load("left_crosshair.png");
    let right_crosshair = asset_server.load("right_crosshair.png");

    commands
        .spawn(Node {
            position_type: PositionType::Absolute,
            width: Val::Percent(100.0),
            height: Val::Percent(100.0),
            ..default()
        })
        .with_children(|mut root| {
            // Unity Crosshair: 100 px image at localScale 0.2 = 20 px.
            root.spawn((
                HudElement::Crosshair,
                ImageNode::new(crosshair),
                Node {
                    position_type: PositionType::Absolute,
                    width: Val::Px(20.0),
                    height: Val::Px(20.0),
                    ..default()
                },
            ));

            // Unity Debug text: anchored top-left (10, -10), 465 x 500,
            // 20 px, colour 0.196, filled from BallMovement every FixedUpdate.
            root.spawn((
                DebugText,
                Node {
                    position_type: PositionType::Absolute,
                    left: Val::Px(10.0),
                    top: Val::Px(10.0),
                    width: Val::Px(465.0),
                    height: Val::Px(500.0),
                    ..default()
                },
                Text::new(""),
                TextFont::from_font_size(20.0),
                TextColor(Color::srgb(0.196, 0.196, 0.196)),
                TextLayout::justify(Justify::Left),
            ));

            // Unity Controls text: anchored top-right (-10, -10), 465 x 500,
            // 20 px, colour 0.196, right-aligned. Text is transcribed verbatim.
            root.spawn((
                Node {
                    position_type: PositionType::Absolute,
                    right: Val::Px(10.0),
                    top: Val::Px(10.0),
                    width: Val::Px(465.0),
                    height: Val::Px(500.0),
                    ..default()
                },
                Text::new(
                    "Movement - WASD\n\nLook - Mouse\n\nBoost (charge) - Space\n\nDodge - Q or E",
                ),
                TextFont::from_font_size(20.0),
                TextColor(Color::srgb(0.196, 0.196, 0.196)),
                TextLayout::justify(Justify::Right),
            ));

            spawn_slider(
                &mut root,
                HudElement::BoostBar,
                100.0,
                Color::srgb(1.0, 0.0, 0.0),
            );
            spawn_slider(
                &mut root,
                HudElement::DodgeBar,
                125.0,
                Color::srgb(0.820_754_7, 0.820_754_7, 0.820_754_7),
            );

            // Unity LeftCursor/RightCursor: 100 px images at localScale 0.2
            // (20 px once GrappleSystem applies baseCursorScale).
            for (element, texture) in [
                (HudElement::HookCursorLeft, left_crosshair.clone()),
                (HudElement::HookCursorRight, right_crosshair.clone()),
            ] {
                root.spawn((
                    element,
                    ImageNode::new(texture),
                    Node {
                        position_type: PositionType::Absolute,
                        width: Val::Px(20.0),
                        height: Val::Px(20.0),
                        ..default()
                    },
                ));
            }

            // Unity GrappleSystem.OnGUI manual-aim ring: yellow, alpha 0.3,
            // radius manualAimRadius * screen height.
            root.spawn((
                HudElement::MouseRing,
                Node {
                    position_type: PositionType::Absolute,
                    width: Val::Px(144.0),
                    height: Val::Px(144.0),
                    border: UiRect::all(Val::Px(2.0)),
                    border_radius: BorderRadius::MAX,
                    ..default()
                },
                BackgroundColor(Color::NONE),
                BorderColor::all(Color::srgba(1.0, 1.0, 0.0, 0.3)),
            ));

            spawn_pause_menu(&mut root);
        });

    // Unity LockOnAdapter creates a world-space 100 px SpriteRenderer at
    // 1.2x scale, tinted green, floating 2.1 m from the enemy toward the camera.
    let lock_texture = asset_server.load("lock_indicator.png");
    let lock_material = materials.add(StandardMaterial {
        base_color: Color::srgb(0.0, 1.0, 0.0),
        base_color_texture: Some(lock_texture),
        alpha_mode: AlphaMode::Blend,
        unlit: true,
        cull_mode: None,
        ..default()
    });
    commands.spawn((
        LockIcon3d,
        Mesh3d(meshes.add(Rectangle::new(1.0, 1.0))),
        MeshMaterial3d(lock_material),
        Transform::default(),
        Visibility::Hidden,
    ));
}

/// One Unity Slider: 160 x 30 at the bottom centre, middle-half background,
/// 150 x 15 fill area and a 20 px handle.
fn spawn_slider(
    root: &mut ChildSpawnerCommands,
    bar_element: HudElement,
    bottom: f32,
    fill_color: Color,
) {
    let (fill_element, handle_element) = match bar_element {
        HudElement::BoostBar => (HudElement::BoostFill, HudElement::BoostHandle),
        _ => (HudElement::DodgeFill, HudElement::DodgeHandle),
    };

    root.spawn((
        bar_element,
        Node {
            position_type: PositionType::Absolute,
            left: Val::Percent(50.0),
            bottom: Val::Px(bottom),
            width: Val::Px(160.0),
            height: Val::Px(30.0),
            margin: UiRect::left(Val::Px(-80.0)),
            ..default()
        },
    ))
    .with_children(|bar| {
        // Background: anchors 0..1 horizontally, 0.25..0.75 vertically.
        bar.spawn((
            Node {
                position_type: PositionType::Absolute,
                left: Val::Px(0.0),
                top: Val::Px(7.5),
                width: Val::Percent(100.0),
                height: Val::Px(15.0),
                ..default()
            },
            BackgroundColor(Color::WHITE),
        ));

        bar.spawn((
            fill_element,
            Node {
                position_type: PositionType::Absolute,
                left: Val::Px(5.0),
                top: Val::Px(7.5),
                width: Val::Px(0.0),
                height: Val::Px(15.0),
                ..default()
            },
            BackgroundColor(fill_color),
        ));

        bar.spawn((
            handle_element,
            Node {
                position_type: PositionType::Absolute,
                left: Val::Px(0.0),
                top: Val::Px(0.0),
                width: Val::Px(20.0),
                height: Val::Px(30.0),
                border_radius: BorderRadius::all(Val::Px(6.0)),
                ..default()
            },
            BackgroundColor(Color::WHITE),
        ));
    });
}

fn spawn_pause_menu(root: &mut ChildSpawnerCommands) {
    // PauseMenu is a transparent 1920 x 1080 panel. Anchored positions below
    // are copied from its RectTransforms and converted from Unity's bottom-up
    // Y axis to Bevy's top-down layout.
    root.spawn((
        HudElement::PauseMenu,
        Node {
            position_type: PositionType::Absolute,
            left: Val::Px(0.0),
            top: Val::Px(0.0),
            width: Val::Percent(100.0),
            height: Val::Percent(100.0),
            display: Display::None,
            ..default()
        },
    ))
    .with_children(|menu| {
        menu.spawn((
            Node {
                position_type: PositionType::Absolute,
                left: Val::Percent(50.0),
                top: Val::Percent(50.0),
                width: Val::Px(887.85),
                height: Val::Px(251.22),
                margin: UiRect {
                    left: Val::Px(-443.925),
                    top: Val::Px(-488.61),
                    ..default()
                },
                justify_content: JustifyContent::Center,
                align_items: AlignItems::Center,
                ..default()
            },
            children![(
                Text::new("PAUSE"),
                TextFont::from_font_size(224.85),
                TextColor(Color::WHITE),
                TextLayout::justify(Justify::Center),
            )],
        ));

        menu.spawn((
            UiButton,
            Node {
                position_type: PositionType::Absolute,
                left: Val::Percent(50.0),
                top: Val::Percent(50.0),
                width: Val::Px(400.0),
                height: Val::Px(165.0),
                margin: UiRect {
                    left: Val::Px(-200.0),
                    top: Val::Px(-82.5),
                    ..default()
                },
                justify_content: JustifyContent::Center,
                align_items: AlignItems::Center,
                ..default()
            },
            children![(
                Text::new("resume"),
                TextFont::from_font_size(100.0),
                TextColor(Color::WHITE),
                TextLayout::justify(Justify::Center),
            )],
            observe(|_: On<Activate>, mut next: ResMut<NextState<GameState>>| {
                next.set(GameState::Playing);
            }),
        ));

        menu.spawn((
            UiButton,
            Node {
                position_type: PositionType::Absolute,
                left: Val::Percent(50.0),
                top: Val::Percent(50.0),
                width: Val::Px(400.0),
                height: Val::Px(165.0),
                margin: UiRect {
                    left: Val::Px(-200.0),
                    top: Val::Px(130.5),
                    ..default()
                },
                justify_content: JustifyContent::Center,
                align_items: AlignItems::Center,
                ..default()
            },
            children![(
                Text::new("restart"),
                TextFont::from_font_size(100.0),
                TextColor(Color::WHITE),
                TextLayout::justify(Justify::Center),
            )],
            observe(|_: On<Activate>, mut next: ResMut<NextState<GameState>>| {
                next.set(GameState::Restarting);
            }),
        ));

        menu.spawn((
            UiButton,
            Node {
                position_type: PositionType::Absolute,
                left: Val::Percent(50.0),
                top: Val::Percent(50.0),
                width: Val::Px(260.0),
                height: Val::Px(150.0),
                margin: UiRect {
                    left: Val::Px(-130.0),
                    top: Val::Px(341.0),
                    ..default()
                },
                justify_content: JustifyContent::Center,
                align_items: AlignItems::Center,
                ..default()
            },
            children![(
                Text::new("quit"),
                TextFont::from_font_size(100.0),
                TextColor(Color::WHITE),
                TextLayout::justify(Justify::Center),
            )],
            observe(|_: On<Activate>, mut exit: MessageWriter<AppExit>| {
                exit.write(AppExit::Success);
            }),
        ));
    });
}

pub fn update_hud(
    state: Res<State<GameState>>,
    stats: Res<GameStats>,
    grapple: Res<GrappleState>,
    lockon: Res<PlayerLockOn>,
    debug: Res<DebugSettings>,
    input: Res<PlayerInput>,
    mouse_motion: Res<AccumulatedMouseMotion>,
    window_query: Query<&Window>,
    camera_query: Query<(&Camera, &GlobalTransform)>,
    player_query: Query<(&Health, &BallMovement), With<Player>>,
    mut text_sets: ParamSet<(
        Query<(&mut Text, &mut Visibility), With<DebugText>>,
        Query<(&mut Transform, &mut Visibility), With<LockIcon3d>>,
    )>,
    mut nodes: Query<(&HudElement, &mut Node)>,
) {
    let Ok(window) = window_query.single() else {
        return;
    };
    let size = window.size().max(Vec2::ONE);
    let camera = camera_query.single().ok();
    let playing = *state.get() == GameState::Playing;
    let paused = *state.get() == GameState::Paused;
    let player = player_query.single().ok();

    let boost = player.map(|(_, m)| m.boost_lv).unwrap_or(0.0);
    let dodge = player.map(|(_, m)| m.dodge_timer).unwrap_or(1.0);
    let charging = player.map(|(_, m)| m.charging).unwrap_or(false);

    for (element, mut node) in &mut nodes {
        match element {
            HudElement::Crosshair => {
                node.left = Val::Px(size.x * 0.5 - 10.0);
                node.top = Val::Px(size.y * 0.5 - 10.0);
            }
            HudElement::BoostBar => {
                node.display = if charging {
                    Display::Flex
                } else {
                    Display::None
                };
            }
            HudElement::BoostFill => {
                node.width = Val::Px((boost * 150.0).clamp(0.0, 150.0));
            }
            HudElement::BoostHandle => {
                node.left = Val::Px((boost * 150.0 - 10.0).clamp(-10.0, 140.0));
            }
            HudElement::DodgeBar => {
                node.display = if dodge < 1.0 {
                    Display::Flex
                } else {
                    Display::None
                };
            }
            HudElement::DodgeFill => {
                node.width = Val::Px((dodge * 150.0).clamp(0.0, 150.0));
            }
            HudElement::DodgeHandle => {
                node.left = Val::Px((dodge * 150.0 - 10.0).clamp(-10.0, 140.0));
            }
            HudElement::MouseRing => {
                // Unity locks the cursor to the window centre during play, so
                // the manual-aim ring and the screen-space aim ray are always
                // centred even after a pause-menu button was clicked.
                let cursor = size * 0.5;
                let radius = grapple.config.manual_aim_radius * size.y;
                let diameter = (radius * 2.0).max(8.0);
                node.width = Val::Px(diameter);
                node.height = Val::Px(diameter);
                node.left = Val::Px(cursor.x - diameter * 0.5);
                node.top = Val::Px(cursor.y - diameter * 0.5);
            }
            HudElement::HookCursorLeft | HudElement::HookCursorRight => {
                let is_left = *element == HudElement::HookCursorLeft;
                let hook = if is_left {
                    &grapple.left
                } else {
                    &grapple.right
                };
                let default_pos = size * 0.5 + Vec2::new(if is_left { -60.0 } else { 60.0 }, 0.0);
                let screen_pos = camera
                    .and_then(|(camera, camera_transform)| {
                        hook.cursor_world.and_then(|world| {
                            camera.world_to_viewport(camera_transform, world).ok()
                        })
                    })
                    .unwrap_or(default_pos);
                let multiplier = match hook.state {
                    HookState::Ready => 1.0,
                    HookState::Firing => 0.7,
                    HookState::Hooked => 1.3,
                    HookState::Returning => 0.5,
                };
                let diameter = 20.0 * multiplier;
                node.width = Val::Px(diameter);
                node.height = Val::Px(diameter);
                node.left = Val::Px(screen_pos.x - diameter * 0.5);
                node.top = Val::Px(screen_pos.y - diameter * 0.5);
            }
            HudElement::PauseMenu => {
                node.display = if paused { Display::Flex } else { Display::None };
            }
        }
    }

    // World-space lock indicator, exactly like LockOnAdapter's SpriteRenderer.
    if let Ok((mut transform, mut visibility)) = text_sets.p1().single_mut() {
        let show = playing && lockon.result.icon_should_show;
        if show {
            if let (Some((_, camera_transform)), Some(enemy)) =
                (camera, lockon.locked_enemy_position)
            {
                let camera_pos = camera_transform.translation();
                let to_camera = (camera_pos - enemy).normalize_or_zero();
                transform.translation = enemy + to_camera * 2.1;
                transform.rotation = Quat::from_rotation_arc(Vec3::Z, to_camera);
                transform.scale = Vec3::splat(1.2);
                *visibility = Visibility::Visible;
            } else {
                *visibility = Visibility::Hidden;
            }
        } else {
            *visibility = Visibility::Hidden;
        }
    }

    // Unity `displayDebugText` is enabled on PlayerBall.prefab. F3 toggles it
    // as a development convenience; the default matches the Unity scene.
    if let Ok((mut text, mut visibility)) = text_sets.p0().single_mut() {
        *visibility = if debug.show_debug_text {
            Visibility::Visible
        } else {
            Visibility::Hidden
        };
        if debug.show_debug_text {
            let (speed, boost_lv, dodge_timer) = player
                .map(|(_, m)| (m.speed, m.boost_lv, m.dodge_timer))
                .unwrap_or_default();
            let (right, forward) = camera
                .map(|(_, transform)| (transform.right(), transform.forward()))
                .unwrap_or((Dir3::X, Dir3::NEG_Z));
            let movement = Vec3::new(input.movement.x, 0.0, input.movement.y);
            let relative =
                movement.x * right.as_vec3() + movement.z * Vec3::new(forward.x, 0.0, forward.z);
            let relative = relative.normalize_or_zero();
            let dodge_input = if input.dodge_left {
                -1.0
            } else if input.dodge_right {
                1.0
            } else {
                0.0
            };
            let mouse = mouse_motion.delta;
            text.0 = format!(
                " player input= ({:.0}, {:.0})\n mouse input= ({:.2}, {:.2})\n \n directionInput= ({:.3}, {:.3}, {:.3})\n \n camera right transform = ({:.3}, {:.3}, {:.3})\n camera forward transform= ({:.3}, {:.3}, {:.3})\n \n relatve direction= ({:.3}, {:.3}, {:.3})\n \n player speed= {:.3}\n \n player boost= {:.3}\n boost FX duration= {:.3}\n \n player dodge= {:.1}\n dodge timer= {:.3}",
                input.movement.x,
                input.movement.y,
                mouse.x,
                mouse.y,
                relative.x,
                relative.y,
                relative.z,
                right.x,
                right.y,
                right.z,
                forward.x,
                forward.y,
                forward.z,
                relative.x,
                relative.y,
                relative.z,
                speed,
                boost_lv,
                stats.speed_fx_timer,
                dodge_input,
                dodge_timer,
            );
        }
    }
}

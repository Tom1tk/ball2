//! HUD, grapple cursors, lock icon, debug overlay and pause/game-over overlay.

use bevy::prelude::*;
use bevy::text::{Justify, TextLayout};

use super::components::*;
use super::grapple::{GrappleState, HookState};
use super::lockon::PlayerLockOn;
use super::state::GameState;

#[derive(Component)]
pub struct HudText;

#[derive(Component)]
pub struct DebugText;

#[derive(Component)]
pub struct BoostBar;

#[derive(Component)]
pub struct BoostFill;

#[derive(Component)]
pub struct DodgeBar;

#[derive(Component)]
pub struct DodgeFill;

#[derive(Component)]
pub struct HookCursor {
    is_left: bool,
}

#[derive(Component)]
pub struct MouseRing;

#[derive(Component)]
pub struct LockIcon;

#[derive(Component)]
pub struct Overlay;

#[derive(Component)]
pub struct OverlayText;

pub fn setup_ui(mut commands: Commands) {
    commands
        .spawn((
            Node {
                position_type: PositionType::Absolute,
                width: Val::Percent(100.0),
                height: Val::Percent(100.0),
                ..default()
            },
        ))
        .with_children(|root| {
            root.spawn((
                Node {
                    position_type: PositionType::Absolute,
                    left: Val::Px(18.0),
                    top: Val::Px(14.0),
                    ..default()
                },
                HudText,
                Text::new("BALL2"),
                TextFont::from_font_size(18.0),
                TextColor(Color::srgb(0.8, 0.95, 1.0)),
            ));

            root.spawn((
                Node {
                    position_type: PositionType::Absolute,
                    right: Val::Px(18.0),
                    top: Val::Px(14.0),
                    ..default()
                },
                DebugText,
                Text::new(""),
                TextFont::from_font_size(13.0),
                TextColor(Color::srgb(0.65, 0.8, 0.9)),
                Visibility::Hidden,
            ));

            // Bottom-centre boost/dodge bars.
            root.spawn(Node {
                position_type: PositionType::Absolute,
                width: Val::Percent(100.0),
                bottom: Val::Px(24.0),
                flex_direction: FlexDirection::Column,
                align_items: AlignItems::Center,
                row_gap: Val::Px(7.0),
                ..default()
            })
            .with_children(|bars| {
                bars.spawn((
                    Node {
                        width: Val::Px(280.0),
                        height: Val::Px(14.0),
                        border: UiRect::all(Val::Px(1.0)),
                        border_radius: BorderRadius::all(Val::Px(4.0)),
                        ..default()
                    },
                    BoostBar,
                    BackgroundColor(Color::srgba(0.02, 0.04, 0.08, 0.8)),
                    BorderColor::all(Color::srgb(0.2, 0.8, 1.0)),
                ))
                .with_children(|bar| {
                    bar.spawn((
                        Node {
                            position_type: PositionType::Absolute,
                            left: Val::Px(0.0),
                            top: Val::Px(0.0),
                            width: Val::Percent(0.0),
                            height: Val::Percent(100.0),
                            border_radius: BorderRadius::all(Val::Px(3.0)),
                            ..default()
                        },
                        BoostFill,
                        BackgroundColor(Color::srgb(0.15, 0.75, 1.0)),
                    ));
                });

                bars.spawn((
                    Node {
                        width: Val::Px(220.0),
                        height: Val::Px(10.0),
                        border: UiRect::all(Val::Px(1.0)),
                        border_radius: BorderRadius::all(Val::Px(3.0)),
                        ..default()
                    },
                    DodgeBar,
                    BackgroundColor(Color::srgba(0.02, 0.04, 0.08, 0.8)),
                    BorderColor::all(Color::srgb(1.0, 0.7, 0.15)),
                ))
                .with_children(|bar| {
                    bar.spawn((
                        Node {
                            position_type: PositionType::Absolute,
                            left: Val::Px(0.0),
                            top: Val::Px(0.0),
                            width: Val::Percent(0.0),
                            height: Val::Percent(100.0),
                            border_radius: BorderRadius::all(Val::Px(2.0)),
                            ..default()
                        },
                        DodgeFill,
                        BackgroundColor(Color::srgb(1.0, 0.65, 0.1)),
                    ));
                });
            });

            // Hook cursors.
            for (is_left, color) in [
                (true, Color::srgb(0.2, 0.95, 1.0)),
                (false, Color::srgb(1.0, 0.55, 0.15)),
            ] {
                root.spawn((
                    Node {
                        position_type: PositionType::Absolute,
                        left: Val::Px(0.0),
                        top: Val::Px(0.0),
                        width: Val::Px(28.0),
                        height: Val::Px(28.0),
                        border: UiRect::all(Val::Px(2.0)),
                        border_radius: BorderRadius::MAX,
                        ..default()
                    },
                    HookCursor { is_left },
                    BackgroundColor(Color::NONE),
                    BorderColor::all(color),
                ));
            }

            // Manual-aim ring at the mouse.
            root.spawn((
                Node {
                    position_type: PositionType::Absolute,
                    left: Val::Px(0.0),
                    top: Val::Px(0.0),
                    width: Val::Px(172.0),
                    height: Val::Px(172.0),
                    border: UiRect::all(Val::Px(1.0)),
                    border_radius: BorderRadius::MAX,
                    ..default()
                },
                MouseRing,
                BackgroundColor(Color::NONE),
                BorderColor::all(Color::srgba(1.0, 1.0, 0.2, 0.35)),
            ));

            // Lock-on icon.
            root.spawn((
                Node {
                    position_type: PositionType::Absolute,
                    left: Val::Px(0.0),
                    top: Val::Px(0.0),
                    width: Val::Px(46.0),
                    height: Val::Px(46.0),
                    border: UiRect::all(Val::Px(3.0)),
                    border_radius: BorderRadius::MAX,
                    display: Display::None,
                    ..default()
                },
                LockIcon,
                BackgroundColor(Color::NONE),
                BorderColor::all(Color::srgb(0.2, 1.0, 0.35)),
            ));

            // Pause / game-over overlay.
            root.spawn((
                Node {
                    position_type: PositionType::Absolute,
                    width: Val::Percent(100.0),
                    height: Val::Percent(100.0),
                    align_items: AlignItems::Center,
                    justify_content: JustifyContent::Center,
                    display: Display::None,
                    ..default()
                },
                Overlay,
                BackgroundColor(Color::srgba(0.0, 0.0, 0.0, 0.72)),
            ))
            .with_children(|overlay| {
                overlay.spawn((
                    Text::new("PAUSED"),
                    TextFont::from_font_size(36.0),
                    TextColor(Color::srgb(0.85, 0.95, 1.0)),
                    TextLayout::justify(Justify::Center),
                    OverlayText,
                ));
            });
        });
}

pub fn update_hud(
    state: Res<State<GameState>>,
    stats: Res<GameStats>,
    grapple: Res<GrappleState>,
    lockon: Res<PlayerLockOn>,
    debug: Res<DebugSettings>,
    window_query: Query<&Window>,
    camera_query: Query<(&Camera, &GlobalTransform)>,
    player_query: Query<(&Health, &BallMovement), With<Player>>,
    mut text_sets: ParamSet<(
        Query<&mut Text, With<HudText>>,
        Query<(&mut Text, &mut Visibility), With<DebugText>>,
        Query<&mut Text, With<OverlayText>>,
    )>,
    mut node_sets: ParamSet<(
        Query<&mut Node, With<BoostBar>>,
        Query<&mut Node, With<BoostFill>>,
        Query<&mut Node, With<DodgeBar>>,
        Query<&mut Node, With<DodgeFill>>,
        Query<&mut Node, With<MouseRing>>,
        Query<&mut Node, With<LockIcon>>,
        Query<&mut Node, With<Overlay>>,
        Query<(&HookCursor, &mut Node, &mut BorderColor)>,
    )>,
) {
    let Ok(window) = window_query.single() else {
        return;
    };
    let size = window.size().max(Vec2::ONE);
    let camera = camera_query.single().ok();
    let playing = *state.get() == GameState::Playing;
    let player = player_query.single().ok();

    if let Some((health, movement)) = player {
        if let Ok(mut text) = text_sets.p0().single_mut() {
            let dodge = if movement.dodge_timer >= 1.0 {
                "READY".to_string()
            } else {
                format!("{:.2}", movement.dodge_timer)
            };
            text.0 = format!(
                "BALL2   HP {}/{}\nENEMIES {}/{}\nSTRAWBERRIES {}/{}\nSPEED {:.1}\nBOOST {:.0}%\nDODGE {}",
                health.current,
                health.max,
                stats.enemies_alive,
                stats.enemies_max.max(1),
                stats.strawberries_collected,
                stats.strawberries_max,
                movement.speed,
                movement.boost_lv * 100.0,
                dodge,
            );
        }

        if let Ok(mut bar) = node_sets.p0().single_mut() {
            bar.display = if movement.charging {
                Display::Flex
            } else {
                Display::None
            };
        }
        if let Ok(mut fill) = node_sets.p1().single_mut() {
            fill.width = Val::Percent((movement.boost_lv * 100.0).clamp(0.0, 100.0));
        }

        if let Ok(mut bar) = node_sets.p2().single_mut() {
            bar.display = if movement.dodge_timer < 1.0 {
                Display::Flex
            } else {
                Display::None
            };
        }
        if let Ok(mut fill) = node_sets.p3().single_mut() {
            fill.width = Val::Percent((movement.dodge_timer * 100.0).clamp(0.0, 100.0));
        }
    }

    if let Ok((mut text, mut visibility)) = text_sets.p1().single_mut() {
        *visibility = if debug.show_debug_text {
            Visibility::Visible
        } else {
            Visibility::Hidden
        };
        if debug.show_debug_text {
            let (speed, grounded) = player
                .map(|(_, m)| (m.speed, m.grounded))
                .unwrap_or_default();
            text.0 = format!(
                "DEBUG\nhooks L:{:?} R:{:?}\nlock target {} icon {}\nspeed {:.1} grounded {}\nreel {:.1}",
                grapple.left.state,
                grapple.right.state,
                lockon.current.target_id,
                lockon.result.icon_should_show,
                speed,
                grounded,
                grapple.reel_velocity,
            );
        }
    }

    // Hook cursors: world aim point if available, otherwise default side offsets.
    if let Some((camera, camera_transform)) = camera {
        let center = size * 0.5;
        for (cursor, mut node, mut border) in &mut node_sets.p7().iter_mut() {
            let hook = if cursor.is_left {
                &grapple.left
            } else {
                &grapple.right
            };
            let default_pos = center + Vec2::new(if cursor.is_left { -72.0 } else { 72.0 }, 0.0);
            let screen_pos = if playing {
                hook.cursor_world
                    .and_then(|world| camera.world_to_viewport(camera_transform, world).ok())
                    .unwrap_or(default_pos)
            } else {
                default_pos
            };

            let multiplier = match hook.state {
                HookState::Ready => 1.0,
                HookState::Firing => 0.7,
                HookState::Hooked => 1.3,
                HookState::Returning => 0.5,
            };
            let diameter = 28.0 * multiplier;
            node.display = if playing {
                Display::Flex
            } else {
                Display::None
            };
            node.width = Val::Px(diameter);
            node.height = Val::Px(diameter);
            node.left = Val::Px(screen_pos.x - diameter * 0.5);
            node.top = Val::Px(screen_pos.y - diameter * 0.5);
            border.set_all(match hook.state {
                HookState::Ready => {
                    if cursor.is_left {
                        Color::srgb(0.2, 0.95, 1.0)
                    } else {
                        Color::srgb(1.0, 0.55, 0.15)
                    }
                }
                HookState::Firing => Color::srgb(1.0, 0.9, 0.2),
                HookState::Hooked => Color::srgb(0.2, 1.0, 0.35),
                HookState::Returning => Color::srgb(0.55, 0.6, 0.7),
            });
        }
    }

    // Manual aim ring follows the mouse.
    if let Ok(mut node) = node_sets.p4().single_mut() {
        node.display = if playing {
            Display::Flex
        } else {
            Display::None
        };
        if playing {
            if let Some(cursor) = window.cursor_position() {
                let radius = grapple.config.manual_aim_radius * size.y;
                let diameter = (radius * 2.0).max(8.0);
                node.width = Val::Px(diameter);
                node.height = Val::Px(diameter);
                node.left = Val::Px(cursor.x - diameter * 0.5);
                node.top = Val::Px(cursor.y - diameter * 0.5);
            }
        }
    }

    // Lock icon over the locked enemy.
    if let Ok(mut node) = node_sets.p5().single_mut() {
        let show = playing && lockon.result.icon_should_show;
        if show {
            if let (Some((camera, camera_transform)), Some(world)) =
                (camera, lockon.locked_enemy_position)
            {
                if let Ok(screen) = camera.world_to_viewport(camera_transform, world) {
                    node.display = Display::Flex;
                    node.left = Val::Px(screen.x - 23.0);
                    node.top = Val::Px(screen.y - 23.0);
                } else {
                    node.display = Display::None;
                }
            } else {
                node.display = Display::None;
            }
        } else {
            node.display = Display::None;
        }
    }

    // Pause/game-over overlay.
    if let Ok(mut node) = node_sets.p6().single_mut() {
        let text = match *state.get() {
            GameState::Paused => Some("PAUSED\n\nEsc  resume\nR  restart\nQ  quit".to_string()),
            GameState::GameOver => Some(match stats.outcome {
                Some(Outcome::Victory) => "VICTORY\n\nR  restart\nQ  quit".to_string(),
                Some(Outcome::Defeat) => "DEFEATED\n\nR  restart\nQ  quit".to_string(),
                None => "GAME OVER\n\nR  restart\nQ  quit".to_string(),
            }),
            _ => None,
        };

        if let Some(text) = text {
            node.display = Display::Flex;
            if let Ok(mut overlay_text) = text_sets.p2().single_mut() {
                overlay_text.0 = text;
            }
        } else {
            node.display = Display::None;
        }
    }
}

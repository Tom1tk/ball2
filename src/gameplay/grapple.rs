//! Dual grapple hook port of `GrappleSystem.cs`.
//!
//! State machine: Ready -> Firing -> Hooked -> Returning -> Ready.
//! Projectiles are simulated as fast travelling entities with a segment raycast
//! each fixed step. Hooked hooks apply the original spring/damper rope force,
//! anti-wall force, reel force and swing anti-gravity.

use avian3d::prelude::*;
use bevy::prelude::*;

use super::components::*;
use super::config::GrappleConfig;
use super::input::PlayerInput;
use super::state::GameState;

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub enum HookState {
    #[default]
    Ready,
    Firing,
    Hooked,
    Returning,
}

#[derive(Clone, Debug)]
pub struct Hook {
    pub state: HookState,
    pub projectile: Option<Entity>,
    pub fire_dir: Vec3,
    pub hook_point: Vec3,
    pub hook_normal: Vec3,
    pub rope_length: f32,
    pub distance: f32,
    pub aim_dir: Vec3,
    pub cursor_world: Option<Vec3>,
    pub auto_target: Option<Vec3>,
    /// The projectile is spawned through deferred commands; its entity does
    /// not exist in the query until the next fixed step.
    pub spawned_this_step: bool,
}

impl Default for Hook {
    fn default() -> Self {
        Self {
            state: HookState::Ready,
            projectile: None,
            fire_dir: Vec3::ZERO,
            hook_point: Vec3::ZERO,
            hook_normal: Vec3::ZERO,
            rope_length: 0.0,
            distance: 0.0,
            aim_dir: Vec3::ZERO,
            cursor_world: None,
            auto_target: None,
            spawned_this_step: false,
        }
    }
}

#[derive(Resource)]
pub struct GrappleState {
    pub config: GrappleConfig,
    pub left: Hook,
    pub right: Hook,
    pub is_reeling: bool,
    pub reel_velocity: f32,
}

impl Default for GrappleState {
    fn default() -> Self {
        Self {
            config: GrappleConfig::default(),
            left: Hook::default(),
            right: Hook::default(),
            is_reeling: false,
            reel_velocity: 0.0,
        }
    }
}

impl GrappleState {
    pub fn any_hooked(&self) -> bool {
        self.left.state == HookState::Hooked || self.right.state == HookState::Hooked
    }
}

#[derive(Resource)]
pub struct GrappleAssets {
    pub projectile_mesh: Handle<Mesh>,
    pub projectile_material: Handle<StandardMaterial>,
    pub indicator_mesh: Handle<Mesh>,
    pub indicator_material: Handle<StandardMaterial>,
    pub rope_meshes: [Handle<Mesh>; 2],
    pub rope_material: Handle<StandardMaterial>,
}

pub fn setup_grapple_assets(
    mut commands: Commands,
    existing: Option<Res<GrappleAssets>>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
) {
    if existing.is_some() {
        return;
    }

    // Unity HookProjectile: built-in cylinder scaled (0.1, 0.3, 0.1), so
    // radius 0.05 and length 0.6, with the built-in default material.
    let projectile_mesh = meshes.add(Cylinder::new(0.05, 0.6));
    let projectile_material = materials.add(StandardMaterial {
        base_color: Color::WHITE,
        perceptual_roughness: 0.5,
        metallic: 0.0,
        ..default()
    });

    // Unity left/right indicators: built-in cube at 0.2 local under the 2x
    // PlayerBall root, i.e. 0.4 m, with the built-in default material.
    let indicator_mesh = meshes.add(Cuboid::new(0.4, 0.4, 0.4));
    let indicator_material = materials.add(StandardMaterial {
        base_color: Color::WHITE,
        perceptual_roughness: 0.5,
        metallic: 0.0,
        ..default()
    });

    let rope_material = materials.add(StandardMaterial {
        base_color: Color::WHITE,
        unlit: true,
        cull_mode: None,
        ..default()
    });
    let rope_meshes = [meshes.add(empty_rope_mesh()), meshes.add(empty_rope_mesh())];

    for (is_left, mesh) in [(true, &rope_meshes[0]), (false, &rope_meshes[1])] {
        commands.spawn((
            RopeVisual { is_left },
            Mesh3d(mesh.clone()),
            MeshMaterial3d(rope_material.clone()),
            Transform::IDENTITY,
            Visibility::Hidden,
        ));
    }

    commands.insert_resource(GrappleAssets {
        projectile_mesh,
        projectile_material,
        indicator_mesh,
        indicator_material,
        rope_meshes,
        rope_material,
    });
}

pub fn update_grapple_aiming(
    state: Res<State<GameState>>,
    mut grapple: ResMut<GrappleState>,
    player_query: Query<(Entity, &Transform), With<Player>>,
    camera_query: Query<(&Camera, &GlobalTransform)>,
    window_query: Query<&Window>,
    spatial: SpatialQuery,
) {
    if *state.get() != GameState::Playing {
        return;
    }
    let Ok((player_entity, player_transform)) = player_query.single() else {
        return;
    };
    let Ok((camera, camera_transform)) = camera_query.single() else {
        return;
    };
    let Ok(window) = window_query.single() else {
        return;
    };

    let player_pos = player_transform.translation;
    let camera_forward = camera_transform.forward().as_vec3().normalize_or_zero();
    let camera_right = camera_transform.right().as_vec3().normalize_or_zero();
    let filter = SpatialQueryFilter::default().with_excluded_entities([player_entity]);
    let ideal_dir = (camera_forward + Vec3::Y).normalize_or_zero();
    // Unity uses CursorLockMode.Locked while playing, so the manual-aim ray
    // always starts at the window centre regardless of where the OS cursor
    // happened to be when the pause menu was dismissed.
    let cursor = Some(window.size() * 0.5);

    let cfg = grapple.config.clone();
    let left_indicator = player_pos - camera_right * cfg.indicator_offset;
    let right_indicator = player_pos + camera_right * cfg.indicator_offset;

    update_aim_for_side(
        &mut grapple.left,
        true,
        &cfg,
        cursor,
        window,
        camera,
        camera_transform,
        &spatial,
        &filter,
        player_pos,
        left_indicator,
        camera_forward,
        ideal_dir,
    );
    update_aim_for_side(
        &mut grapple.right,
        false,
        &cfg,
        cursor,
        window,
        camera,
        camera_transform,
        &spatial,
        &filter,
        player_pos,
        right_indicator,
        camera_forward,
        ideal_dir,
    );
}

#[allow(clippy::too_many_arguments)]
fn update_aim_for_side(
    hook: &mut Hook,
    is_left: bool,
    cfg: &GrappleConfig,
    cursor: Option<Vec2>,
    window: &Window,
    camera: &Camera,
    camera_transform: &GlobalTransform,
    spatial: &SpatialQuery,
    filter: &SpatialQueryFilter,
    player_pos: Vec3,
    indicator_pos: Vec3,
    camera_forward: Vec3,
    ideal_dir: Vec3,
) {
    if hook.state != HookState::Ready {
        hook.cursor_world = None;
        hook.auto_target = None;
        return;
    }

    hook.auto_target = None;

    // Manual aim: ray through the mouse; accept only if the hit is inside the
    // small screen-space manual radius (the yellow ring in the Unity HUD).
    if let Some(cursor_pos) = cursor {
        if let Ok(ray) = camera.viewport_to_world(camera_transform, cursor_pos) {
            if let Some(hit) =
                spatial.cast_ray(ray.origin, ray.direction, cfg.max_range, true, filter)
            {
                let point = ray.origin + *ray.direction * hit.distance;
                if let Ok(hit_viewport) = camera.world_to_viewport(camera_transform, point) {
                    let size = window.size().max(Vec2::ONE);
                    let screen_distance = (hit_viewport / size).distance(cursor_pos / size);
                    if screen_distance < cfg.manual_aim_radius {
                        hook.aim_dir = (point - indicator_pos).normalize_or_zero();
                        hook.cursor_world = Some(point);
                        return;
                    }
                }
            }
        }
    }

    // Auto-aim: scan a small viewport grid on the relevant half of the screen.
    if let Some(target) = find_auto_aim_target(
        is_left,
        window,
        camera,
        camera_transform,
        spatial,
        filter,
        player_pos,
        cfg.max_range,
        ideal_dir,
    ) {
        hook.aim_dir = (target - indicator_pos).normalize_or_zero();
        hook.cursor_world = Some(target);
        hook.auto_target = Some(target);
        return;
    }

    // Default: camera-forward raycast from the indicator.
    hook.aim_dir = camera_forward;
    hook.cursor_world = spatial
        .cast_ray(
            indicator_pos,
            Dir3::new(camera_forward).unwrap_or(Dir3::Z),
            cfg.max_range,
            true,
            filter,
        )
        .map(|hit| indicator_pos + camera_forward * hit.distance);
}

#[allow(clippy::too_many_arguments)]
fn find_auto_aim_target(
    is_left: bool,
    window: &Window,
    camera: &Camera,
    camera_transform: &GlobalTransform,
    spatial: &SpatialQuery,
    filter: &SpatialQueryFilter,
    player_pos: Vec3,
    max_range: f32,
    ideal_dir: Vec3,
) -> Option<Vec3> {
    const Y_STEPS: usize = 8;
    const X_STEPS: usize = 5;
    let size = window.size().max(Vec2::ONE);
    let mut best_score = 0.0;
    let mut best_target = None;

    for yi in 0..Y_STEPS {
        let vp_y = (yi as f32 + 0.5) / Y_STEPS as f32;
        for xi in 0..X_STEPS {
            let base_x = (xi as f32 + 0.5) / (X_STEPS as f32 * 2.0);
            let vp_x = if is_left { base_x } else { 1.0 - base_x };
            let viewport = Vec2::new(vp_x, vp_y) * size;
            let Ok(ray) = camera.viewport_to_world(camera_transform, viewport) else {
                continue;
            };
            let Some(hit) = spatial.cast_ray(ray.origin, ray.direction, max_range, true, filter)
            else {
                continue;
            };
            let hit_point = ray.origin + *ray.direction * hit.distance;
            if hit_point.y <= player_pos.y + 0.5 {
                continue;
            }
            let point = hit_point + hit.normal * 0.3;
            let to_point = point - player_pos;
            if to_point.length_squared() > max_range * max_range {
                continue;
            }
            let mut score = to_point.dot(ideal_dir);
            if vp_y > 0.66 {
                score *= 2.0;
            }
            if score > best_score {
                best_score = score;
                best_target = Some(point);
            }
        }
    }

    best_target
}

/// Keep the two grapple-base cubes on either side of the player ball.
///
/// They are separate world entities rather than children of the rolling ball,
/// so they stay camera-left/right like Unity's indicators while following the
/// interpolated ball transform every render frame. This avoids the 50 Hz snap
/// the fixed-step world-space version had.
pub fn update_indicator_transforms(
    grapple: Res<GrappleState>,
    player_query: Query<&Transform, With<Player>>,
    camera_query: Query<&GlobalTransform, (With<Camera>, Without<Player>)>,
    mut indicators: Query<
        (&HookIndicator, &mut Transform),
        (Without<Player>, Without<Camera>, Without<HookProjectile>),
    >,
) {
    let Ok(player_transform) = player_query.single() else {
        return;
    };
    let Ok(camera_transform) = camera_query.single() else {
        return;
    };
    let camera_right = camera_transform.right().as_vec3().normalize_or_zero();
    let camera_forward = camera_transform.forward().as_vec3().normalize_or_zero();
    let player_pos = player_transform.translation;
    let offset = grapple.config.indicator_offset;

    for (indicator, mut transform) in &mut indicators {
        let side = if indicator.is_left { -1.0 } else { 1.0 };
        let position = player_pos + camera_right * (offset * side);
        transform.translation = position;
        transform.look_at(position + camera_forward, Vec3::Y);
    }
}

pub fn update_grapple(
    time: Res<Time<Fixed>>,
    input: Res<PlayerInput>,
    state: Res<State<GameState>>,
    assets: Res<GrappleAssets>,
    mut commands: Commands,
    mut grapple: ResMut<GrappleState>,
    mut player_query: Query<(Entity, &Transform, Forces, &mut BallState, &Health), With<Player>>,
    camera_query: Query<&GlobalTransform, With<Camera>>,
    mut projectiles: Query<
        &mut Transform,
        (
            With<HookProjectile>,
            Without<Player>,
            Without<HookIndicator>,
        ),
    >,
    spatial: SpatialQuery,
) {
    if *state.get() != GameState::Playing {
        return;
    }
    let Ok((player_entity, player_transform, mut forces, mut ball_state, health)) =
        player_query.single_mut()
    else {
        return;
    };
    if health.dead {
        return;
    }
    let camera_transform = camera_query.iter().next();
    let camera_forward = camera_transform
        .map(|t| t.forward().as_vec3())
        .unwrap_or(Vec3::NEG_Z);
    let camera_right = camera_transform
        .map(|t| t.right().as_vec3())
        .unwrap_or(Vec3::X);

    let player_pos = player_transform.translation;
    let dt = time.delta_secs();
    let filter = SpatialQueryFilter::default().with_excluded_entities([player_entity]);

    let cfg = grapple.config.clone();
    // Keep the rope sources on the same camera-left/right bases as the
    // indicator cubes; the visual cubes are updated in `Update` for smoothness.
    let left_indicator_pos = player_pos - camera_right * cfg.indicator_offset;
    let right_indicator_pos = player_pos + camera_right * cfg.indicator_offset;

    grapple.is_reeling = input.reel_held;

    // Firing / releasing. The Unity system used InputAction started/canceled
    // callbacks; these flags are the fixed-step snapshot of those events.
    let left_released_hooked = handle_fire_release(
        &mut grapple.left,
        input.fire_left,
        input.release_left,
        left_indicator_pos,
        camera_forward,
        &assets,
        &mut commands,
    );
    let right_released_hooked = handle_fire_release(
        &mut grapple.right,
        input.fire_right,
        input.release_right,
        right_indicator_pos,
        camera_forward,
        &assets,
        &mut commands,
    );
    if left_released_hooked || right_released_hooked {
        *forces.linear_velocity_mut() *= cfg.release_boost_multiplier;
    }

    // Move hook projectiles, resolve rope hits, return or hold.
    advance_hook(
        &mut grapple.left,
        &cfg,
        dt,
        left_indicator_pos,
        &spatial,
        &filter,
        &mut projectiles,
        &mut commands,
    );
    advance_hook(
        &mut grapple.right,
        &cfg,
        dt,
        right_indicator_pos,
        &spatial,
        &filter,
        &mut projectiles,
        &mut commands,
    );

    // Release boost / velocity retention is applied at release time in Unity.
    // (The 1.2x multiplier is handled in handle_fire_release before state change.)

    // Rope constraints + anti-wall force.
    for hook in [&grapple.left, &grapple.right] {
        if hook.state != HookState::Hooked {
            continue;
        }
        let to_hook = hook.hook_point - player_pos;
        let distance = to_hook.length();
        if distance > 0.001 {
            let dir = to_hook / distance;
            if distance > hook.rope_length {
                let overshoot = distance - hook.rope_length;
                let velocity_along = forces.linear_velocity().dot(dir);
                let force = cfg.rope_stiffness * overshoot - cfg.rope_damping * velocity_along;
                forces.apply_linear_acceleration(dir * force);
            }
        }
        forces.apply_linear_acceleration(hook.hook_normal * cfg.anti_wall_force);
    }

    // Reel.
    let left_hooked = grapple.left.state == HookState::Hooked;
    let right_hooked = grapple.right.state == HookState::Hooked;
    if !grapple.is_reeling || (!left_hooked && !right_hooked) {
        grapple.reel_velocity = (grapple.reel_velocity - cfg.reel_acceleration * 2.0 * dt).max(0.0);
    } else {
        grapple.reel_velocity =
            (grapple.reel_velocity + cfg.reel_acceleration * dt).min(cfg.max_reel_speed);
        let mut reel_dir = Vec3::ZERO;
        let mut hooked_count = 0;

        if left_hooked {
            let dir = (grapple.left.hook_point - player_pos).normalize_or_zero();
            reel_dir += dir;
            hooked_count += 1;
            grapple.left.rope_length -= grapple.reel_velocity * dt;
            if grapple.left.rope_length <= cfg.min_rope_length {
                grapple.left.state = HookState::Returning;
            }
        }
        if right_hooked {
            let dir = (grapple.right.hook_point - player_pos).normalize_or_zero();
            reel_dir += dir;
            hooked_count += 1;
            grapple.right.rope_length -= grapple.reel_velocity * dt;
            if grapple.right.rope_length <= cfg.min_rope_length {
                grapple.right.state = HookState::Returning;
            }
        }

        if hooked_count > 0 {
            reel_dir /= hooked_count as f32;
            forces.apply_linear_acceleration(reel_dir * grapple.reel_velocity * 5.0);
        }
    }

    // Both hooks near-opposite -> release to avoid a rigid "stuck" tangle.
    if grapple.left.state == HookState::Hooked && grapple.right.state == HookState::Hooked {
        let dir_l = (grapple.left.hook_point - player_pos).normalize_or_zero();
        let dir_r = (grapple.right.hook_point - player_pos).normalize_or_zero();
        if dir_l.length_squared() > 0.0
            && dir_r.length_squared() > 0.0
            && dir_l.angle_between(dir_r).to_degrees() >= cfg.stuck_angle_threshold
        {
            grapple.left.state = HookState::Returning;
            grapple.right.state = HookState::Returning;
        }
    }

    // Swing anti-gravity.
    if grapple.any_hooked() {
        forces.apply_linear_acceleration(Vec3::Y * cfg.gravity_magnitude * cfg.swing_anti_gravity);
    }

    ball_state.speed = forces.linear_velocity().length();
}

#[allow(clippy::too_many_arguments)]
fn handle_fire_release(
    hook: &mut Hook,
    fire: bool,
    release: bool,
    indicator_pos: Vec3,
    camera_forward: Vec3,
    assets: &GrappleAssets,
    commands: &mut Commands,
) -> bool {
    let mut released_hooked = false;
    if fire && hook.state == HookState::Ready {
        let mut fire_dir = hook.aim_dir;
        if fire_dir == Vec3::ZERO {
            fire_dir = camera_forward;
        }
        hook.fire_dir = fire_dir.normalize_or_zero();
        hook.distance = 0.0;
        hook.state = HookState::Firing;
        hook.spawned_this_step = true;

        let entity = commands
            .spawn((
                GameWorld,
                HookProjectile,
                Mesh3d(assets.projectile_mesh.clone()),
                MeshMaterial3d(assets.projectile_material.clone()),
                Transform::from_translation(indicator_pos),
                Visibility::Visible,
            ))
            .id();
        hook.projectile = Some(entity);
    }

    if release && (hook.state == HookState::Firing || hook.state == HookState::Hooked) {
        released_hooked = hook.state == HookState::Hooked;
        hook.state = HookState::Returning;
    }

    released_hooked
}

#[allow(clippy::too_many_arguments)]
fn advance_hook(
    hook: &mut Hook,
    cfg: &GrappleConfig,
    dt: f32,
    indicator_pos: Vec3,
    spatial: &SpatialQuery,
    filter: &SpatialQueryFilter,
    projectiles: &mut Query<
        &mut Transform,
        (
            With<HookProjectile>,
            Without<Player>,
            Without<HookIndicator>,
        ),
    >,
    commands: &mut Commands,
) {
    if hook.state == HookState::Firing {
        // The spawn command is deferred until the end of this fixed step.
        if hook.spawned_this_step {
            hook.spawned_this_step = false;
            return;
        }
        let Some(projectile) = hook.projectile else {
            hook.state = HookState::Ready;
            return;
        };
        let Ok(mut transform) = projectiles.get_mut(projectile) else {
            hook.state = HookState::Ready;
            hook.projectile = None;
            return;
        };

        let step = cfg.projectile_speed * dt;
        let previous = transform.translation;
        let next = previous + hook.fire_dir * step;
        hook.distance += step;

        if let Some(hit) = spatial.cast_ray(
            previous,
            Dir3::new(hook.fire_dir).unwrap_or(Dir3::Z),
            step,
            true,
            filter,
        ) {
            let point = previous + hook.fire_dir * hit.distance;
            hook.hook_point = point;
            hook.hook_normal = hit.normal;
            hook.rope_length = indicator_pos.distance(point);
            hook.state = HookState::Hooked;
            transform.translation = point;
        } else if hook.distance >= cfg.max_range {
            hook.state = HookState::Returning;
            transform.translation = next;
        } else {
            transform.translation = next;
        }
    } else if hook.state == HookState::Hooked {
        if let Some(projectile) = hook.projectile {
            if let Ok(mut transform) = projectiles.get_mut(projectile) {
                transform.translation = hook.hook_point;
            }
        }
    } else if hook.state == HookState::Returning {
        let Some(projectile) = hook.projectile else {
            hook.state = HookState::Ready;
            return;
        };
        let Ok(mut transform) = projectiles.get_mut(projectile) else {
            hook.state = HookState::Ready;
            hook.projectile = None;
            return;
        };

        let to_target = indicator_pos - transform.translation;
        let return_step = cfg.return_speed * dt;
        if to_target.length() <= return_step {
            commands.entity(projectile).despawn();
            hook.projectile = None;
            hook.state = HookState::Ready;
        } else {
            transform.translation += to_target.normalize_or_zero() * return_step;
        }
    }
}

fn empty_rope_mesh() -> Mesh {
    use bevy::asset::RenderAssetUsages;
    use bevy::mesh::Indices;
    use bevy::render::render_resource::PrimitiveTopology;

    Mesh::new(
        PrimitiveTopology::TriangleList,
        RenderAssetUsages::MAIN_WORLD | RenderAssetUsages::RENDER_WORLD,
    )
    .with_inserted_attribute(Mesh::ATTRIBUTE_POSITION, vec![[0.0f32; 3]; 4])
    .with_inserted_attribute(Mesh::ATTRIBUTE_NORMAL, vec![[0.0f32, 0.0, 1.0]; 4])
    .with_inserted_attribute(
        Mesh::ATTRIBUTE_UV_0,
        vec![[0.0f32, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]],
    )
    .with_inserted_indices(Indices::U32(vec![0, 1, 2, 0, 2, 3]))
}

/// Rebuild the two world-space ribbon meshes that replace Unity's
/// `LineRenderer`s. Unity uses `alignment = View` and a 0.5361 m width.
pub fn update_rope_visuals(
    grapple: Res<GrappleState>,
    assets: Res<GrappleAssets>,
    player_query: Query<(&Transform, &Health), With<Player>>,
    projectile_query: Query<&Transform, With<HookProjectile>>,
    camera_query: Query<&GlobalTransform, With<Camera>>,
    mut ropes: Query<(&RopeVisual, &mut Visibility), Without<HookIndicator>>,
    mut indicators: Query<(&HookIndicator, &mut Visibility), Without<RopeVisual>>,
    mut meshes: ResMut<Assets<Mesh>>,
) {
    use bevy::mesh::Indices;

    let Ok((player_transform, player_health)) = player_query.single() else {
        return;
    };

    // Unity disables the PlayerBall GameObject, which also hides the child
    // LeftIndicator/RightIndicator transforms and LineRenderers.
    if player_health.dead {
        for (_, mut visibility) in &mut ropes {
            *visibility = Visibility::Hidden;
        }
        for (_, mut visibility) in &mut indicators {
            *visibility = Visibility::Hidden;
        }
        return;
    }
    for (_, mut visibility) in &mut indicators {
        *visibility = Visibility::Visible;
    }
    let camera_transform = camera_query.iter().next();
    let camera_right = camera_transform
        .map(|t| t.right().as_vec3())
        .unwrap_or(Vec3::X);
    let camera_position = camera_transform
        .map(|t| t.translation())
        .unwrap_or(Vec3::ZERO);
    let player_pos = player_transform.translation;
    let cfg = &grapple.config;

    let left_indicator = player_pos - camera_right * cfg.indicator_offset;
    let right_indicator = player_pos + camera_right * cfg.indicator_offset;

    for (rope, mut visibility) in &mut ropes {
        let (hook, start) = if rope.is_left {
            (&grapple.left, left_indicator)
        } else {
            (&grapple.right, right_indicator)
        };

        let end = if hook.state == HookState::Hooked {
            Some(hook.hook_point)
        } else if let Some(projectile) = hook.projectile {
            projectile_query
                .get(projectile)
                .ok()
                .map(|transform| transform.translation)
        } else {
            None
        };

        let Some(end) = end else {
            *visibility = Visibility::Hidden;
            continue;
        };
        *visibility = Visibility::Visible;

        let along = end - start;
        if along.length_squared() < 1e-8 {
            continue;
        }
        let view = camera_position - start;
        let mut side = along.cross(view).normalize_or_zero();
        if side.length_squared() < 1e-8 {
            side = camera_right.normalize_or_zero();
        }
        let side = side * (0.536_133_5 * 0.5);
        let positions = vec![
            (start - side).to_array(),
            (start + side).to_array(),
            (end + side).to_array(),
            (end - side).to_array(),
        ];

        let index = if rope.is_left { 0 } else { 1 };
        let Some(mut mesh) = meshes.get_mut(&assets.rope_meshes[index]) else {
            continue;
        };
        mesh.insert_attribute(Mesh::ATTRIBUTE_POSITION, positions.clone());
        mesh.insert_attribute(
            Mesh::ATTRIBUTE_NORMAL,
            vec![
                [0.0, 0.0, 1.0],
                [0.0, 0.0, 1.0],
                [0.0, 0.0, 1.0],
                [0.0, 0.0, 1.0],
            ],
        );
        mesh.insert_attribute(
            Mesh::ATTRIBUTE_UV_0,
            vec![[0.0f32, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]],
        );
        mesh.insert_indices(Indices::U32(vec![0, 1, 2, 0, 2, 3]));
    }
}

/// Draw debug-only aim markers with Bevy gizmos (F3).
pub fn draw_grapple_gizmos(
    grapple: Res<GrappleState>,
    debug: Res<DebugSettings>,
    player_query: Query<&Transform, With<Player>>,
    camera_query: Query<&GlobalTransform, With<Camera>>,
    mut gizmos: Gizmos,
) {
    if !debug.show_aim_gizmos {
        return;
    }
    let Ok(player_transform) = player_query.single() else {
        return;
    };
    let camera_transform = camera_query.iter().next();
    let camera_right = camera_transform
        .map(|t| t.right().as_vec3())
        .unwrap_or(Vec3::X);
    let player_pos = player_transform.translation;
    let cfg = &grapple.config;

    let left_indicator = player_pos - camera_right * cfg.indicator_offset;
    let right_indicator = player_pos + camera_right * cfg.indicator_offset;

    for (hook, start) in [
        (&grapple.left, left_indicator),
        (&grapple.right, right_indicator),
    ] {
        if let Some(target) = hook.auto_target {
            gizmos.sphere(
                Isometry3d::from_translation(target),
                0.25,
                Color::srgb(0.1, 1.0, 1.0),
            );
        }
        if let Some(cursor) = hook.cursor_world {
            gizmos.line(start, cursor, Color::srgb(1.0, 1.0, 0.0));
        }
    }

    gizmos.cross(
        Isometry3d::from_translation(player_pos),
        0.6,
        if grapple.any_hooked() {
            Color::srgb(0.2, 1.0, 0.2)
        } else {
            Color::srgb(1.0, 1.0, 0.2)
        },
    );
}

//! World spawning: lighting, camera, arena, balls, pickups and restart cleanup.
//!
//! Arena geometry, spawn points and lighting are transcribed from the enabled
//! Unity scene `Assets/Scenes/Test2.unity` (the 500 x 500 m course, its 41
//! box colliders and the single EnemyBall spawn).

use avian3d::prelude::*;
use bevy::prelude::*;
use bevy_hanabi::ParticleEffect;

use super::components::*;
use super::config::{BallConfig, HealthSettings};
use super::effects::FxAssets;
use super::enemy::{ArenaBounds, EnemyAi};
use super::grapple::{GrappleAssets, GrappleState};
use super::lockon::PlayerLockOn;
use super::state::GameState;

const ARENA_BLUE: usize = 0;
const ARENA_PINK: usize = 1;

/// `(material index, world position, world rotation, world scale)` for every
/// BoxCollider in Test2.unity. The Unity cube primitive is 1 m, so the
/// transform scale is also the collider size.
const ARENA_BLOCKS: &[(usize, Vec3, Quat, Vec3)] = &[
    (
        ARENA_PINK,
        Vec3::new(-35.55, 28.4, -36.3698),
        Quat::from_xyzw(0.0, -0.6437068, 0.0, 0.7652722),
        Vec3::new(10.0, 60.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-2.42, 104.51, 44.8),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(5.0, 0.5, 30.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-26.7, 101.1, 8.7),
        Quat::from_xyzw(0.12278779, -0.6963643, 0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-2.4, 104.51, -43.6),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(5.0, 0.5, 30.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(21.92, 101.1, -7.05),
        Quat::from_xyzw(0.12278779, 0.6963643, -0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-250.0, 1.0, 0.0),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(1.0, 20.0, 500.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(0.0, 1.0, -250.0),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(1.0, 20.0, 500.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-64.8, 95.5, -68.5),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 10.0, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-2.42, 104.51, 8.7),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(5.0, 0.5, 30.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(0.0, 4.9, 33.4),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 10.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-13.8, 2.2, 33.4),
        Quat::from_xyzw(-0.11122528, 0.69830436, 0.11122528, 0.69830436),
        Vec3::new(5.0, 2.0, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(28.64, 28.4, 22.66),
        Quat::from_xyzw(0.0, 0.9051066, 0.0, 0.42518473),
        Vec3::new(10.0, 60.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-26.68, 101.1, -7.05),
        Quat::from_xyzw(0.12278779, -0.6963643, 0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-144.4, 33.4, -101.3),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-4.73, 20.0, -40.92),
        Quat::from_xyzw(0.0, -0.7184247, 0.0, 0.69560486),
        Vec3::new(10.0, 40.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-103.8, 33.4, 106.1),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(31.4, 95.5, 85.5),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 10.0, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-40.9, 33.4, -14.55),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-26.7, 101.1, 44.8),
        Quat::from_xyzw(0.12278779, -0.6963643, 0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-22.2, 33.4, 77.6),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(15.82, 9.4, 31.74),
        Quat::from_xyzw(0.0, 0.80586547, 0.0, 0.5920986),
        Vec3::new(5.0, 0.5, 15.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-55.3, 95.5, 51.1),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 10.0, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(21.9, 101.1, 8.7),
        Quat::from_xyzw(0.12278779, 0.6963643, -0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(44.5, 33.4, 189.1),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(67.9, 95.5, -8.6),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 10.0, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-26.68, 101.1, -43.6),
        Quat::from_xyzw(0.12278779, -0.6963643, 0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-41.5746, 67.06, -28.97),
        Quat::from_xyzw(-0.0028022758, -0.9846792, 0.17362742, -0.01589307),
        Vec3::new(5.0, 0.5, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(9.6725, 0.0, -42.04),
        Quat::from_xyzw(-0.10941633, -0.7094816, -0.11300659, 0.68694496),
        Vec3::new(5.0, 2.0, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(21.92, 101.1, -43.6),
        Quat::from_xyzw(0.12278779, 0.6963643, -0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-20.5953, 41.99, -40.06),
        Quat::from_xyzw(0.0, -0.60502684, 0.0, 0.7962051),
        Vec3::new(5.0, 0.5, 15.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(0.0, 1.0, 250.0),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(1.0, 20.0, 500.0),
    ),
    (
        ARENA_BLUE,
        Vec3::new(0.0, -5.0, 0.0),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(500.0, 10.0, 500.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(14.8, 33.4, -172.6),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(250.0, 1.0, 0.0),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(1.0, 20.0, 500.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(36.2, 48.8, 0.45),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(10.0, 100.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(36.27, 3.92, 15.01),
        Quat::from_xyzw(0.17364816, 0.0, 0.0, 0.9848078),
        Vec3::new(5.0, 0.5, 10.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(21.9, 101.1, 44.8),
        Quat::from_xyzw(0.12278779, 0.6963643, -0.12278779, 0.6963643),
        Vec3::new(5.0, 0.5, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(122.5, 33.4, 124.0),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(27.8, 33.4, 80.6),
        Quat::from_xyzw(0.0, 0.0, 0.0, 1.0),
        Vec3::new(10.0, 70.0, 20.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(-2.4, 104.51, -7.05),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(5.0, 0.5, 30.0),
    ),
    (
        ARENA_PINK,
        Vec3::new(151.8, 33.4, -74.4),
        Quat::from_xyzw(0.0, 0.7071068, 0.0, 0.7071068),
        Vec3::new(10.0, 70.0, 20.0),
    ),
];

/// Unity (left-handed, +Z forward) to Bevy (right-handed, -Z forward)
/// conversion: mirror Z for positions and conjugate rotations with the same
/// reflection. Applying it to both the world and the camera preserves the
/// authored layout instead of showing a mirrored copy of the scene.
fn unity_pos(p: Vec3) -> Vec3 {
    Vec3::new(p.x, p.y, -p.z)
}

fn unity_rot(q: Quat) -> Quat {
    Quat::from_xyzw(-q.x, -q.y, q.z, q.w)
}

pub fn setup_persistent(mut commands: Commands, mut clear_color: ResMut<ClearColor>) {
    // Unity Test2 RenderSettings ambient sky/equator colours and default skybox.
    clear_color.0 = Color::srgb(0.192, 0.302, 0.475);

    commands.insert_resource(GlobalAmbientLight {
        color: Color::srgb(0.55, 0.62, 0.72),
        brightness: 260.0,
        ..default()
    });

    // Directional Light transform from Test2.unity, warm colour + 1.3 intensity.
    commands.spawn((
        DirectionalLight {
            color: Color::srgb(1.0, 0.956, 0.839),
            illuminance: 10_000.0,
            shadow_maps_enabled: true,
            ..default()
        },
        // Unity light rotation converted into the Bevy basis. Bevy lights
        // shine along local -Z, which is the converted Unity +Z direction.
        Transform::from_rotation(unity_rot(Quat::from_xyzw(
            0.40821788,
            -0.23456968,
            0.10938163,
            0.8754261,
        ))),
    ));

    // Unity camera: 75 degree FOV, 0.1..5000 clip planes.
    commands.spawn((
        Camera3d::default(),
        Camera::default(),
        Projection::Perspective(PerspectiveProjection {
            fov: 75.0_f32.to_radians(),
            near: 0.1,
            far: 5000.0,
            ..default()
        }),
        Msaa::Sample4,
        Transform::from_translation(unity_pos(Vec3::new(-0.561, 3.9, -11.987))).with_rotation(
            unity_rot(Quat::from_xyzw(
                0.064344235,
                0.02333831,
                -0.0015052176,
                0.9976537,
            )),
        ),
        CameraFollow::default(),
    ));
}

pub fn spawn_world(
    mut commands: Commands,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
    asset_server: Res<AssetServer>,
    mut world: ResMut<WorldSpawned>,
    mut stats: ResMut<GameStats>,
    mut grapple: ResMut<GrappleState>,
    mut lockon: ResMut<PlayerLockOn>,
    ball_cfg: Res<BallConfig>,
    health_settings: Res<HealthSettings>,
    grapple_assets: Res<GrappleAssets>,
    fx: Res<FxAssets>,
) {
    if world.0 {
        return;
    }
    world.0 = true;

    *stats = GameStats::default();
    *lockon = PlayerLockOn::default();
    grapple.left = Default::default();
    grapple.right = Default::default();
    grapple.is_reeling = false;
    grapple.reel_velocity = 0.0;

    commands.insert_resource(ArenaBounds {
        half_extents: Vec3::new(250.0, 150.0, 250.0),
        floor_y: 0.0,
    });

    let cube = meshes.add(Cuboid::new(1.0, 1.0, 1.0));
    let shell_mesh = meshes.add(Sphere::new(ball_cfg.shell_radius).mesh().ico(4).unwrap());
    let core_mesh = meshes.add(Sphere::new(ball_cfg.core_radius).mesh().ico(3).unwrap());

    let arena_textures = [
        asset_server.load("arena_blue.png"),
        asset_server.load("arena_pink.png"),
    ];
    let arena_materials = [
        materials.add(StandardMaterial {
            base_color: Color::WHITE,
            base_color_texture: Some(arena_textures[ARENA_BLUE].clone()),
            perceptual_roughness: 0.5,
            metallic: 0.0,
            ..default()
        }),
        materials.add(StandardMaterial {
            base_color: Color::WHITE,
            base_color_texture: Some(arena_textures[ARENA_PINK].clone()),
            perceptual_roughness: 0.5,
            metallic: 0.0,
            ..default()
        }),
    ];

    let shell_textures = [
        asset_server.load("ball.png"),
        asset_server.load("ball_dmg1.png"),
        asset_server.load("ball_dmg2.png"),
        asset_server.load("ball_dmg3.png"),
        asset_server.load("ball.png"),
    ];

    let static_material = Friction {
        dynamic_coefficient: 0.6,
        static_coefficient: 0.6,
        combine_rule: CoefficientCombine::Max,
    };

    for &(material, position, rotation, size) in ARENA_BLOCKS {
        spawn_block(
            &mut commands,
            &cube,
            &arena_materials[material],
            unity_pos(position),
            unity_rot(rotation),
            size,
            static_material,
        );
    }

    // Player spawn is the unmodified PlayerBall prefab instance in Test2.unity.
    let player = spawn_ball(
        &mut commands,
        &mut materials,
        &shell_mesh,
        &core_mesh,
        unity_pos(Vec3::new(0.0, 1.4, 0.0)),
        health_settings.player_max_health,
        Color::srgb(0.1, 0.75, 1.0),
        ball_cfg.gravity_scale,
        &ball_cfg,
        &shell_textures,
    );
    commands
        .entity(player)
        .insert((Player, BallMovement::default()));
    commands.spawn((
        ChildOf(player),
        PhaseFx,
        ParticleEffect::new(fx.phase.clone()),
        Transform::IDENTITY,
    ));

    // Grapple indicator meshes are world entities (they follow the player).
    commands.spawn((
        GameWorld,
        HookIndicator { is_left: true },
        Mesh3d(grapple_assets.indicator_mesh.clone()),
        MeshMaterial3d(grapple_assets.indicator_material.clone()),
        Transform::from_xyz(-1.5, 3.0, 0.0),
        Visibility::Visible,
    ));
    commands.spawn((
        GameWorld,
        HookIndicator { is_left: false },
        Mesh3d(grapple_assets.indicator_mesh.clone()),
        MeshMaterial3d(grapple_assets.indicator_material.clone()),
        Transform::from_xyz(1.5, 3.0, 0.0),
        Visibility::Visible,
    ));

    // The only EnemyBall in Test2.unity, at its serialized spawn point.
    let enemy = spawn_ball(
        &mut commands,
        &mut materials,
        &shell_mesh,
        &core_mesh,
        unity_pos(Vec3::new(-25.2, 2.3, 27.8)),
        health_settings.enemy_max_health,
        Color::srgb(1.0, 0.28, 0.12),
        1.0,
        &ball_cfg,
        &shell_textures,
    );
    commands.entity(enemy).insert((Enemy, EnemyAi::new()));
    stats.enemies_max = 1;
    stats.enemies_alive = 1;
}

fn spawn_block(
    commands: &mut Commands,
    cube: &Handle<Mesh>,
    material: &Handle<StandardMaterial>,
    position: Vec3,
    rotation: Quat,
    size: Vec3,
    friction: Friction,
) {
    commands.spawn((
        GameWorld,
        RigidBody::Static,
        Collider::cuboid(1.0, 1.0, 1.0),
        friction,
        Restitution::ZERO,
        Mesh3d(cube.clone()),
        MeshMaterial3d(material.clone()),
        Transform::from_translation(position)
            .with_rotation(rotation)
            .with_scale(size),
        Visibility::Visible,
    ));
}

#[allow(clippy::too_many_arguments)]
fn spawn_ball(
    commands: &mut Commands,
    materials: &mut Assets<StandardMaterial>,
    shell_mesh: &Handle<Mesh>,
    core_mesh: &Handle<Mesh>,
    position: Vec3,
    max_health: i32,
    intact_color: Color,
    gravity_scale: f32,
    ball_cfg: &BallConfig,
    shell_textures: &[Handle<Image>; 5],
) -> Entity {
    // Unity BallTex: white texture * color alpha 0.392, Standard fade mode.
    let shell_material = materials.add(StandardMaterial {
        base_color: Color::srgba(1.0, 1.0, 1.0, 0.392),
        base_color_texture: Some(shell_textures[0].clone()),
        alpha_mode: AlphaMode::Blend,
        perceptual_roughness: 0.7,
        metallic: 0.0,
        ..default()
    });
    // Unity child Sphere uses SimpleBlue until speed/charge swapping.
    let core_material = materials.add(StandardMaterial {
        base_color: Color::srgb(0.0, 0.604_660_03, 1.0),
        perceptual_roughness: 0.5,
        metallic: 0.0,
        ..default()
    });

    let entity = commands
        .spawn((
            GameWorld,
            Ball,
            RigidBody::Dynamic,
            Collider::sphere(ball_cfg.radius),
            Mass(1.0),
            GravityScale(gravity_scale),
            LinearDamping(0.0),
            AngularDamping(20.0),
            Friction {
                dynamic_coefficient: 2.0,
                static_coefficient: 0.1,
                combine_rule: CoefficientCombine::Max,
            },
            Restitution {
                coefficient: 0.5,
                combine_rule: CoefficientCombine::Min,
            },
            CollisionEventsEnabled,
            Mesh3d(shell_mesh.clone()),
            MeshMaterial3d(shell_material.clone()),
            Transform::from_translation(position),
            Visibility::Visible,
        ))
        .insert((
            Health::new(max_health, intact_color),
            BallState::default(),
            PrePhysicsVelocity::default(),
            // Fast boosts can exceed 50 m/s; sweep against static geometry so
            // the 1 m thick closed-box walls cannot be tunnelled through.
            SweptCcd::default(),
        ))
        .id();

    commands.spawn((
        ChildOf(entity),
        Mesh3d(core_mesh.clone()),
        MeshMaterial3d(core_material.clone()),
        Transform::IDENTITY,
        // Inherit from the ball root so Unity's SetActive(false) hides the
        // inner sphere along with the shell on player death.
        Visibility::Inherited,
    ));

    commands.entity(entity).insert(BallVisual {
        shell_material,
        shell_textures: shell_textures.clone(),
        core_material,
        charge_fx: None,
    });

    entity
}

pub fn despawn_world(
    mut commands: Commands,
    query: Query<Entity, With<GameWorld>>,
    mut camera: Query<(&mut Transform, &mut CameraFollow), With<Camera>>,
    mut world: ResMut<WorldSpawned>,
    mut stats: ResMut<GameStats>,
    mut grapple: ResMut<GrappleState>,
    mut lockon: ResMut<PlayerLockOn>,
    mut next: ResMut<NextState<GameState>>,
    mut fx: ResMut<super::effects::FxState>,
) {
    for entity in &query {
        commands.entity(entity).despawn();
    }

    // Unity's restart reloads the scene, resetting MainCamera to its authored
    // transform and CameraFollow to the serialized yaw/pitch.
    if let Ok((mut camera_tf, mut follow)) = camera.single_mut() {
        camera_tf.translation = unity_pos(Vec3::new(-0.561, 3.9, -11.987));
        camera_tf.rotation = unity_rot(Quat::from_xyzw(
            0.064344235,
            0.02333831,
            -0.0015052176,
            0.9976537,
        ));
        *follow = CameraFollow::default();
    }

    // SpeedFX is parented to the camera and survives the GameWorld cleanup;
    // clear it explicitly so a restart cannot leave a stale effect behind.
    if let Some(effect) = fx.speed_entity.take() {
        commands.entity(effect).despawn();
    }
    world.0 = false;
    *stats = GameStats::default();
    *grapple = GrappleState::default();
    *lockon = PlayerLockOn::default();
    *fx = super::effects::FxState::default();
    next.set(GameState::Playing);
}

pub fn pickup_collisions(
    mut events: MessageReader<CollisionStart>,
    mut commands: Commands,
    mut stats: ResMut<GameStats>,
    health_settings: Res<HealthSettings>,
    mut player_health: Query<&mut Health, With<Player>>,
    player_query: Query<Entity, With<Player>>,
    pickups: Query<(), With<Pickup>>,
) {
    let Ok(player_entity) = player_query.single() else {
        return;
    };
    let Ok(mut health) = player_health.single_mut() else {
        return;
    };

    let mut consumed: Vec<Entity> = Vec::new();
    for event in events.read() {
        let entities = [
            event.collider1,
            event.collider2,
            event.body1.unwrap_or(event.collider1),
            event.body2.unwrap_or(event.collider2),
        ];
        let Some(pickup) = entities.iter().copied().find(|e| pickups.contains(*e)) else {
            continue;
        };
        let other_is_player = entities.iter().any(|e| *e == player_entity && *e != pickup);
        if !other_is_player || consumed.contains(&pickup) {
            continue;
        }
        consumed.push(pickup);
        health.heal(health_settings.strawberry_heal);
        stats.strawberries_collected = stats.strawberries_collected.saturating_add(1);
        commands.entity(pickup).despawn();
    }
}

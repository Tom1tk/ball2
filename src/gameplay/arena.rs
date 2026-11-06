//! World spawning: lighting, camera, arena, balls, pickups and restart cleanup.

use avian3d::prelude::*;
use bevy::prelude::*;

use super::components::*;
use super::config::{BallConfig, HealthSettings};
use super::enemy::{ArenaBounds, EnemyAi};
use super::grapple::{GrappleAssets, GrappleState};
use super::lockon::PlayerLockOn;
use super::state::GameState;

pub fn setup_persistent(mut commands: Commands, mut clear_color: ResMut<ClearColor>) {
    clear_color.0 = Color::srgb(0.015, 0.02, 0.045);

    commands.insert_resource(GlobalAmbientLight {
        color: Color::srgb(0.55, 0.65, 0.9),
        brightness: 260.0,
        ..default()
    });

    commands.spawn((
        DirectionalLight {
            illuminance: 9000.0,
            shadow_maps_enabled: true,
            ..default()
        },
        Transform::from_xyz(20.0, 40.0, 15.0).looking_at(Vec3::ZERO, Vec3::Y),
    ));

    commands.spawn((
        Camera3d::default(),
        Camera::default(),
        Msaa::Sample4,
        Transform::from_xyz(0.0, 5.0, 10.0).looking_at(Vec3::new(0.0, 3.0, 0.0), Vec3::Y),
        CameraFollow::default(),
    ));
}

pub fn spawn_world(
    mut commands: Commands,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
    mut world: ResMut<WorldSpawned>,
    mut stats: ResMut<GameStats>,
    mut grapple: ResMut<GrappleState>,
    mut lockon: ResMut<PlayerLockOn>,
    ball_cfg: Res<BallConfig>,
    health_settings: Res<HealthSettings>,
    grapple_assets: Res<GrappleAssets>,
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
        half_extents: Vec3::new(60.0, 30.0, 60.0),
        floor_y: 0.0,
    });

    let cube = meshes.add(Cuboid::new(1.0, 1.0, 1.0));
    let shell_mesh = meshes.add(Sphere::new(ball_cfg.radius).mesh().ico(4).unwrap());
    let core_mesh = meshes.add(Sphere::new(ball_cfg.core_radius).mesh().ico(3).unwrap());
    let charge_mesh = meshes.add(Torus::new(1.05, 1.15).mesh().build());
    let pickup_mesh = meshes.add(Sphere::new(0.5).mesh().ico(3).unwrap());

    let floor_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.03, 0.05, 0.09),
        perceptual_roughness: 0.85,
        metallic: 0.15,
        ..default()
    });
    let wall_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.06, 0.1, 0.2),
        perceptual_roughness: 0.7,
        metallic: 0.25,
        emissive: LinearRgba::rgb(0.01, 0.02, 0.06),
        ..default()
    });
    let pillar_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.16, 0.08, 0.28),
        perceptual_roughness: 0.45,
        metallic: 0.35,
        emissive: LinearRgba::rgb(0.04, 0.01, 0.1),
        ..default()
    });
    let platform_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.05, 0.24, 0.28),
        perceptual_roughness: 0.4,
        metallic: 0.3,
        emissive: LinearRgba::rgb(0.01, 0.08, 0.1),
        ..default()
    });
    let accent_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.8, 0.25, 0.08),
        emissive: LinearRgba::rgb(1.2, 0.25, 0.05),
        perceptual_roughness: 0.3,
        metallic: 0.4,
        ..default()
    });
    let strawberry_mat = materials.add(StandardMaterial {
        base_color: Color::srgb(0.9, 0.04, 0.12),
        emissive: LinearRgba::rgb(2.4, 0.1, 0.2),
        perceptual_roughness: 0.35,
        ..default()
    });

    let static_material = Friction {
        dynamic_coefficient: 0.6,
        static_coefficient: 0.6,
        combine_rule: CoefficientCombine::Max,
    };

    // Floor + ceiling + four walls = a closed box, per the design spec.
    spawn_block(
        &mut commands,
        &cube,
        &floor_mat,
        Vec3::new(0.0, -0.5, 0.0),
        Vec3::new(120.0, 1.0, 120.0),
        static_material,
    );
    spawn_block(
        &mut commands,
        &cube,
        &wall_mat,
        Vec3::new(0.0, 60.5, 0.0),
        Vec3::new(120.0, 1.0, 120.0),
        static_material,
    );
    for (pos, size) in [
        (Vec3::new(-60.5, 30.0, 0.0), Vec3::new(1.0, 60.0, 120.0)),
        (Vec3::new(60.5, 30.0, 0.0), Vec3::new(1.0, 60.0, 120.0)),
        (Vec3::new(0.0, 30.0, -60.5), Vec3::new(120.0, 60.0, 1.0)),
        (Vec3::new(0.0, 30.0, 60.5), Vec3::new(120.0, 60.0, 1.0)),
    ] {
        spawn_block(&mut commands, &cube, &wall_mat, pos, size, static_material);
    }

    // Pillars and grapple surfaces.
    for (pos, size) in [
        (Vec3::new(-24.0, 12.0, -18.0), Vec3::new(10.0, 24.0, 10.0)),
        (Vec3::new(24.0, 18.0, 18.0), Vec3::new(8.0, 36.0, 8.0)),
        (Vec3::new(32.0, 8.0, -30.0), Vec3::new(12.0, 16.0, 12.0)),
        (Vec3::new(-34.0, 20.0, 20.0), Vec3::new(10.0, 40.0, 10.0)),
        (Vec3::new(-38.0, 6.0, -34.0), Vec3::new(16.0, 12.0, 16.0)),
        (Vec3::new(16.0, 26.0, -24.0), Vec3::new(6.0, 52.0, 6.0)),
        (Vec3::new(38.0, 30.0, 4.0), Vec3::new(10.0, 60.0, 10.0)),
        (Vec3::new(0.0, 1.0, -20.0), Vec3::new(20.0, 2.0, 20.0)),
    ] {
        spawn_block(
            &mut commands,
            &cube,
            &pillar_mat,
            pos,
            size,
            static_material,
        );
    }

    // Floating platforms/slabs. The central spawn shaft is deliberately clear.
    for (pos, size) in [
        (Vec3::new(-26.0, 36.0, 16.0), Vec3::new(18.0, 1.0, 18.0)),
        (Vec3::new(26.0, 44.0, -16.0), Vec3::new(18.0, 1.0, 18.0)),
        (Vec3::new(0.0, 50.0, 26.0), Vec3::new(24.0, 1.0, 24.0)),
        (Vec3::new(-22.0, 52.0, -28.0), Vec3::new(16.0, 1.0, 16.0)),
    ] {
        spawn_block(
            &mut commands,
            &cube,
            &platform_mat,
            pos,
            size,
            static_material,
        );
    }

    // Small floating anchor cubes.
    for (pos, size) in [
        (Vec3::new(12.0, 38.0, 12.0), Vec3::new(4.0, 4.0, 4.0)),
        (Vec3::new(-12.0, 46.0, 8.0), Vec3::new(4.0, 4.0, 4.0)),
        (Vec3::new(8.0, 54.0, -8.0), Vec3::new(4.0, 4.0, 4.0)),
    ] {
        spawn_block(
            &mut commands,
            &cube,
            &accent_mat,
            pos,
            size,
            static_material,
        );
    }

    // Player.
    let player = spawn_ball(
        &mut commands,
        &mut materials,
        &shell_mesh,
        &core_mesh,
        &charge_mesh,
        Vec3::new(0.0, ball_cfg.radius + 0.05, 0.0),
        health_settings.player_max_health,
        Color::srgb(0.1, 0.75, 1.0),
        ball_cfg.gravity_scale,
        &ball_cfg,
    );
    commands
        .entity(player)
        .insert((Player, BallMovement::default()));

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

    // Enemies.
    for (x, z) in [(-20.0, 20.0), (20.0, -20.0), (-15.0, -35.0)] {
        let position = Vec3::new(x, ball_cfg.radius + 0.05, z);
        let enemy = spawn_ball(
            &mut commands,
            &mut materials,
            &shell_mesh,
            &core_mesh,
            &charge_mesh,
            position,
            health_settings.enemy_max_health,
            Color::srgb(1.0, 0.28, 0.12),
            1.0,
            &ball_cfg,
        );
        commands.entity(enemy).insert((Enemy, EnemyAi::new()));
    }
    stats.enemies_max = 3;
    stats.enemies_alive = 3;

    // Strawberry pickups (sensor triggers).
    let strawberry_positions = [
        Vec3::new(12.0, 1.2, 12.0),
        Vec3::new(-12.0, 1.2, 12.0),
        Vec3::new(12.0, 1.2, -12.0),
        Vec3::new(-12.0, 1.2, -12.0),
        Vec3::new(0.0, 27.2, 0.0),
        Vec3::new(-26.0, 37.2, 16.0),
        Vec3::new(26.0, 45.2, -16.0),
        Vec3::new(0.0, 51.2, 26.0),
    ];
    stats.strawberries_max = strawberry_positions.len() as u32;
    for position in strawberry_positions {
        commands.spawn((
            GameWorld,
            Pickup,
            RigidBody::Static,
            Collider::sphere(0.6),
            Sensor,
            CollisionEventsEnabled,
            Mesh3d(pickup_mesh.clone()),
            MeshMaterial3d(strawberry_mat.clone()),
            Transform::from_translation(position),
            Visibility::Visible,
            Spin {
                radians_per_second: 1.6,
            },
        ));
    }
}

fn spawn_block(
    commands: &mut Commands,
    cube: &Handle<Mesh>,
    material: &Handle<StandardMaterial>,
    position: Vec3,
    size: Vec3,
    friction: Friction,
) {
    commands.spawn((
        GameWorld,
        RigidBody::Static,
        Collider::cuboid(0.5, 0.5, 0.5),
        friction,
        Restitution::ZERO,
        Mesh3d(cube.clone()),
        MeshMaterial3d(material.clone()),
        Transform::from_translation(position).with_scale(size),
        Visibility::Visible,
    ));
}

#[allow(clippy::too_many_arguments)]
fn spawn_ball(
    commands: &mut Commands,
    materials: &mut Assets<StandardMaterial>,
    shell_mesh: &Handle<Mesh>,
    core_mesh: &Handle<Mesh>,
    charge_mesh: &Handle<Mesh>,
    position: Vec3,
    max_health: i32,
    intact_color: Color,
    gravity_scale: f32,
    ball_cfg: &BallConfig,
) -> Entity {
    let shell_material = materials.add(StandardMaterial {
        base_color: intact_color.with_alpha(0.39),
        alpha_mode: AlphaMode::Blend,
        perceptual_roughness: 0.15,
        metallic: 0.1,
        ..default()
    });
    let core_material = materials.add(StandardMaterial {
        base_color: Color::srgb(0.75, 0.95, 1.0),
        emissive: LinearRgba::rgb(0.6, 1.6, 2.2),
        perceptual_roughness: 0.1,
        metallic: 0.2,
        ..default()
    });
    let charge_material = materials.add(StandardMaterial {
        base_color: Color::srgb(1.0, 0.2, 0.85),
        emissive: LinearRgba::rgb(2.5, 0.3, 1.8),
        alpha_mode: AlphaMode::Add,
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

    let core = commands
        .spawn((
            ChildOf(entity),
            Mesh3d(core_mesh.clone()),
            MeshMaterial3d(core_material.clone()),
            Transform::IDENTITY,
            Visibility::Visible,
        ))
        .id();

    let charge_fx = commands
        .spawn((
            ChildOf(entity),
            ChargeFx,
            Mesh3d(charge_mesh.clone()),
            MeshMaterial3d(charge_material.clone()),
            Transform::IDENTITY,
            Visibility::Hidden,
        ))
        .id();

    commands.entity(entity).insert(BallVisual {
        shell_material,
        core_material,
        charge_fx,
        charge_material,
    });

    let _ = core;
    entity
}

pub fn despawn_world(
    mut commands: Commands,
    query: Query<Entity, With<GameWorld>>,
    mut world: ResMut<WorldSpawned>,
    mut stats: ResMut<GameStats>,
    mut grapple: ResMut<GrappleState>,
    mut lockon: ResMut<PlayerLockOn>,
    mut trail: ResMut<SpeedTrail>,
    mut next: ResMut<NextState<GameState>>,
) {
    for entity in &query {
        commands.entity(entity).despawn();
    }

    world.0 = false;
    *stats = GameStats::default();
    *grapple = GrappleState::default();
    *lockon = PlayerLockOn::default();
    *trail = SpeedTrail::default();
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
